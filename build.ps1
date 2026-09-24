<#
.SYNOPSIS
    Test, publish and package Azure Subnet Planner as an MSI.
.EXAMPLE
    ./build.ps1 -Version 1.2.0
    Creates artifacts\msi\AzureSubnetPlanner.msi
#>
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'artifacts\publish'
$msiDir = Join-Path $root 'artifacts\msi'

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)" }
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

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

Invoke-Step 'Build MSI' {
    dotnet build (Join-Path $root 'installer\AzureSubnetPlanner.Installer.wixproj') `
        -c $Configuration -p:ProductVersion=$Version -o $msiDir
}

$msi = Get-ChildItem $msiDir -Filter *.msi | Select-Object -First 1
Write-Host "MSI gereed: $($msi.FullName)" -ForegroundColor Green
