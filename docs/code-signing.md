# Code signing

`build.ps1` can Authenticode-sign both `AzureSubnetPlanner.exe` (before it goes into the MSI) and the MSI itself, and verifies both signatures. The GitHub Actions workflow signs automatically as soon as one of the options below is configured; until then it keeps producing unsigned builds. Pull request builds are never signed.

| | Azure Artifact Signing (recommended) | Your own certificate (.pfx) |
| --- | --- | --- |
| What it is | Microsoft-managed signing service (formerly *Trusted Signing*); certificates are created, stored and renewed by Azure | OV/EV code signing certificate bought from a CA (DigiCert, Sectigo, GlobalSign, …) |
| Cost | Basic plan ≈ USD 10 per month | ≈ USD 200–600 per year |
| Private key | Never leaves Azure (HSM) | Exported to a .pfx file stored as a GitHub secret. Since June 2023 CAs issue new code signing keys only on hardware tokens/HSMs, so a .pfx is usually only possible with a cloud HSM or an older certificate |
| Availability | At the time of writing: organizations in the US, Canada, EU and UK (identity validation required) | Worldwide |

## What signing does and does not do

- **Windows shows a verified publisher** (your organization name) in the UAC prompt and the file properties, instead of *Unknown publisher*.
- **SmartScreen**: signed files build reputation per certificate. A brand-new certificate can still trigger the *Windows protected your PC* warning until enough people have installed the signed MSI; after that the warning disappears. Since 2024 this also applies to EV certificates. SmartScreen only checks files downloaded from the internet (a browser or e-mail), so installs through Intune don't show it at all.
- **Intune**: Intune does not require signed MSIs, but signing lets you allow the app by publisher in App Control for Business (WDAC) or AppLocker policies, and lets users and admins verify where the installer came from.

## Option 1: Azure Artifact Signing

### 1. Create the Artifact Signing resources (one-time)

1. In the Azure Portal, register the resource provider `Microsoft.CodeSigning` on the subscription.
2. Create an **Artifact Signing account** (for example in West Europe). Note the account name and the **Account URI**, for example `https://weu.codesigning.azure.net/`.
3. In the account, create an **identity validation** for your organization (public trust) and wait for approval. Microsoft may ask for company documents; this can take a few business days.
4. Create a **certificate profile** of type *Public Trust* that uses the approved identity validation. Note the profile name.

### 2. Give GitHub Actions access (no secrets needed)

1. In Microsoft Entra ID, create an **app registration** (for example `github-azure-vnet-advisor-signing`). Note the *Application (client) ID* and *Directory (tenant) ID*.
2. In the app registration, go to *Certificates & secrets → Federated credentials → Add credential*:
   - Scenario: **GitHub Actions deploying Azure resources**
   - Organization: `AkoSti0578`, Repository: `Azure-VNET-advisor`
   - Entity type: **Environment**, name: `signing`
3. On the certificate profile (or the Artifact Signing account), go to *Access control (IAM)* and assign the role **Artifact Signing Certificate Profile Signer** to the app registration.

### 3. Configure the GitHub repository

In *Settings → Secrets and variables → Actions*:

| Type | Name | Value |
| --- | --- | --- |
| Variable | `ARTIFACT_SIGNING_ENDPOINT` | Account URI, for example `https://weu.codesigning.azure.net/` |
| Variable | `ARTIFACT_SIGNING_ACCOUNT` | Artifact Signing account name |
| Variable | `ARTIFACT_SIGNING_PROFILE` | Certificate profile name |
| Secret | `AZURE_CLIENT_ID` | Application (client) ID of the app registration |
| Secret | `AZURE_TENANT_ID` | Directory (tenant) ID |

The next push builds a signed MSI. The build log shows `Signing method: ArtifactSigning` and the output of `signtool verify`.

### Signing locally

Requires the Windows SDK (for `signtool.exe`), the Azure CLI, and the *Artifact Signing Certificate Profile Signer* role for your own account:

```powershell
az login
$env:ARTIFACT_SIGNING_ENDPOINT = 'https://weu.codesigning.azure.net/'
$env:ARTIFACT_SIGNING_ACCOUNT  = '<account name>'
$env:ARTIFACT_SIGNING_PROFILE  = '<certificate profile name>'
./build.ps1 -Version 1.0.8 -Sign ArtifactSigning
```

## Option 2: Your own certificate (.pfx)

1. Convert the certificate to base64:
   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('codesigning.pfx')) | Set-Clipboard
   ```
2. In *Settings → Secrets and variables → Actions*, add the secrets `SIGNING_PFX_BASE64` (the clipboard contents) and `SIGNING_PFX_PASSWORD`.
3. Optional: add the variable `SIGNING_TIMESTAMP_URL` if your CA uses a different timestamp server than `http://timestamp.digicert.com`.

The workflow writes the certificate to a temporary file only for the duration of the build and deletes it afterwards. If both options are configured, Artifact Signing is used.

Signing locally:

```powershell
$env:SIGNING_PFX_PATH = 'C:\certs\codesigning.pfx'
$env:SIGNING_PFX_PASSWORD = Read-Host 'PFX password'
./build.ps1 -Version 1.0.8 -Sign Pfx
```

## Checking a signed MSI

Right-click the MSI → *Properties* → *Digital Signatures*, or:

```powershell
Get-AuthenticodeSignature .\AzureSubnetPlanner.msi | Format-List Status, SignerCertificate, TimeStamperCertificate
```

`Status` should be `Valid`. Thanks to the timestamp, the signature stays valid after the certificate expires.
