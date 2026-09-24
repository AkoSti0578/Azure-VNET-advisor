<#
.SYNOPSIS
    Test, publish, sign and package Azure Subnet Planner as an MSI.

.DESCRIPTION
    Signing is optional. With -Sign, both AzureSubnetPlanner.exe (before it is packed into
    the MSI) and the MSI itself are Authenticode-signed with signtool and time-stamped.
    Signing settings are read from environment variables so secrets never appear on the
    command line or in logs:

    -Sign ArtifactSigning  (Azure Artifact Signing, formerly Trusted Signing)
        ARTIFACT_SIGNING_ENDPOINT   e.g. https://weu.codesigning.azure.net/
        ARTIFACT_SIGNING_ACCOUNT    Artifact Signing account name
        ARTIFACT_SIGNING_PROFILE    certificate profile name
        Authentication uses DefaultAzureCredential: 'az login', a GitHub OIDC login
        (azure/login) or AZURE_CLIENT_ID / AZURE_TENANT_ID / AZURE_CLIENT_SECRET.

    -Sign Pfx  (code signing certificate in a .pfx file)
        SIGNING_PFX_PATH            path to the .pfx file
        SIGNING_PFX_PASSWORD        password of the .pfx file
        SIGNING_TIMESTAMP_URL       optional, default http://timestamp.digicert.com

.EXAMPLE
    ./build.ps1 -Version 1.2.0
    Creates an unsigned artifacts\msi\AzureSubnetPlanner.msi

.EXAMPLE
    az login
    $env:ARTIFACT_SIGNING_ENDPOINT = 'https://weu.codesigning.azure.net/'
    $env:ARTIFACT_SIGNING_ACCOUNT  = 'my-signing-account'
    $env:ARTIFACT_SIGNING_PROFILE  = 'my-public-trust-profile'
    ./build.ps1 -Version 1.2.0 -Sign ArtifactSigning
#>
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [ValidateSet('None', 'ArtifactSigning', 'Pfx')]
    [string]$Sign = 'None',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifactsDir = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifactsDir 'publish'
$msiDir = Join-Path $artifactsDir 'msi'
$signingToolsDir = Join-Path $artifactsDir 'signing-tools'

# Version of the Microsoft.ArtifactSigning.Client NuGet package (signtool plug-in).
$artifactSigningClientVersion = '1.0.128'

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)" }
}

function Get-RequiredEnv([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Environment variable $Name is required for -Sign $Sign." }
    return $value
}

function Find-SignTool {
    $candidates = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending
    if (-not $candidates) { throw 'signtool.exe not found. Install the Windows SDK (Signing Tools for Desktop Apps).' }
    return $candidates[0].FullName
}

# Returns the signtool arguments (without the file name) for the selected signing method.
function Initialize-Signing {
    $common = @('sign', '/v', '/fd', 'SHA256', '/td', 'SHA256', '/d', 'Azure Subnet Planner')

    if ($Sign -eq 'ArtifactSigning') {
        $dlib = Join-Path $signingToolsDir 'bin\x64\Azure.CodeSigning.Dlib.dll'
        if (-not (Test-Path $dlib)) {
            Write-Host "Downloading Microsoft.ArtifactSigning.Client $artifactSigningClientVersion"
            New-Item -ItemType Directory -Force -Path $signingToolsDir | Out-Null
            $package = Join-Path $signingToolsDir 'client.zip'
            Invoke-WebRequest -UseBasicParsing -OutFile $package `
                -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.artifactsigning.client/$artifactSigningClientVersion/microsoft.artifactsigning.client.$artifactSigningClientVersion.nupkg"
            Expand-Archive -Path $package -DestinationPath $signingToolsDir -Force
        }

        $metadataPath = Join-Path $signingToolsDir 'metadata.json'
        [ordered]@{
            Endpoint               = Get-RequiredEnv 'ARTIFACT_SIGNING_ENDPOINT'
            CodeSigningAccountName = Get-RequiredEnv 'ARTIFACT_SIGNING_ACCOUNT'
            CertificateProfileName = Get-RequiredEnv 'ARTIFACT_SIGNING_PROFILE'
        } | ConvertTo-Json | Set-Content -Path $metadataPath -Encoding utf8

        return $common + @('/tr', 'http://timestamp.acs.microsoft.com', '/dlib', $dlib, '/dmdf', $metadataPath)
    }

    $pfx = Get-RequiredEnv 'SIGNING_PFX_PATH'
    if (-not (Test-Path $pfx)) { throw "Certificate file '$pfx' (SIGNING_PFX_PATH) not found." }
    $timestampUrl = [Environment]::GetEnvironmentVariable('SIGNING_TIMESTAMP_URL')
    if ([string]::IsNullOrWhiteSpace($timestampUrl)) { $timestampUrl = 'http://timestamp.digicert.com' }
    return $common + @('/tr', $timestampUrl, '/f', $pfx, '/p', (Get-RequiredEnv 'SIGNING_PFX_PASSWORD'))
}

function Invoke-Sign([string]$File) {
    Invoke-Step "Sign $(Split-Path $File -Leaf)" { & $script:signTool @script:signArguments $File | Out-Host }
    Invoke-Step "Verify signature of $(Split-Path $File -Leaf)" { & $script:signTool verify /pa /v $File | Out-Host }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

if ($Sign -ne 'None') {
    $script:signTool = Find-SignTool
    $script:signArguments = Initialize-Signing
    Write-Host "Signing with $Sign using $script:signTool"
}

if (-not $SkipTests) {
    Invoke-Step 'Unit tests' {
        dotnet test (Join-Path $root 'tests\AzureSubnetPlanner.Core.Tests') -c $Configuration
    }
}

Invoke-Step 'Publish app (self-contained, single file)' {
    dotnet publish (Join-Path $root 'src\AzureSubnetPlanner.App\AzureSubnetPlanner.App.csproj') `
        -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:Version=$Version -o $publishDir
}

if ($Sign -ne 'None') {
    Invoke-Sign (Join-Path $publishDir 'AzureSubnetPlanner.exe')
}

Invoke-Step 'Build MSI' {
    dotnet build (Join-Path $root 'installer\AzureSubnetPlanner.Installer.wixproj') `
        -c $Configuration -p:ProductVersion=$Version -o $msiDir
}

$msi = Get-ChildItem $msiDir -Filter *.msi | Select-Object -First 1

if ($Sign -ne 'None') {
    Invoke-Sign $msi.FullName
} else {
    Write-Warning 'The MSI is not signed. Use -Sign ArtifactSigning or -Sign Pfx for a signed build.'
}

Write-Host "MSI ready: $($msi.FullName)" -ForegroundColor Green
