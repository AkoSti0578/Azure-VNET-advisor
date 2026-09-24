# Azure Subnet Planner

Windows application (WPF, .NET 8) that recommends which **address prefix** to use for a new Azure VNET, without overlapping:

- **existing Azure VNETs** – read from a CSV export of Azure Resource Graph;
- **local / on-premises networks** – entered by you (office, datacenter, VPN);
- **ranges reserved by Azure** – 127.0.0.0/8, 169.254.0.0/16, 168.63.129.16/32, 224.0.0.0/4 and 255.255.255.255/32.

It also proposes a **subnet layout** inside the new VNET (including GatewaySubnet, AzureBastionSubnet, AzureFirewallSubnet, …) and accounts for the 5 addresses Azure reserves in every subnet.

The application does not connect to Azure or the internet; all data stays on your machine.

## How to use

1. **Export existing VNETs** – open *Resource Graph Explorer* in the Azure Portal, run the query in [`kql/existing-vnets.kql`](kql/existing-vnets.kql) (also available via the *Copy KQL query* button in the app) and choose *Download as CSV*.
2. **Import the CSV** – click *Import CSV...*. Comma, semicolon and tab separated files are supported; IPv6 prefixes are skipped.
3. **Enter local networks** – one CIDR per line, text after `#` is a description:
   ```
   192.168.0.0/16  # Office
   172.16.10.0/24  # Datacenter
   ```
4. **Search range** – the address space to search, in order of preference (default `10.0.0.0/8`). For each range the app shows how much space is still free.
5. **New VNET** – pick a fixed size (e.g. /22) or let the size be calculated from the subnets you need (optionally with room for growth).
6. Click **Recommend address prefix**. You get the recommended prefix plus alternatives, each with a subnet layout that you can copy, export as CSV or copy as Azure CLI commands.

Additional tabs:

- **Check prefix** – check a prefix of your own and see exactly which VNETs/networks it overlaps.
- **Occupied address space** – filterable overview of all occupied ranges.
- **Warnings** – import warnings and existing overlaps between VNETs and/or local networks.

Local networks, search ranges and the last used CSV file are saved in `%APPDATA%\AzureSubnetPlanner\settings.json`.

## Building the MSI

### With GitHub Actions

The workflow [`.github/workflows/build-msi.yml`](.github/workflows/build-msi.yml) runs on every push on `windows-latest`, runs the tests and publishes the MSI as a build artifact (*Actions → Build MSI → Artifacts*). Push a tag such as `v1.2.0` to create a GitHub Release containing the MSI.

### Code signing

The MSI and the application can be signed with Azure Artifact Signing or your own code signing certificate. The workflow signs automatically once signing is configured; see [docs/code-signing.md](docs/code-signing.md) for the setup.

### Locally (Windows)

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). WiX Toolset is restored automatically from NuGet.

```powershell
./build.ps1 -Version 1.0.0
```

Add `-Sign ArtifactSigning` or `-Sign Pfx` for a signed build.

Output: `artifacts\msi\AzureSubnetPlanner.msi`. The app is published self-contained, so no .NET runtime is needed on the target machine. The MSI installs per machine into `C:\Program Files\Azure Subnet Planner` with shortcuts in the Start menu and on the desktop; a newer version automatically replaces the old one.

To just run the app during development:

```powershell
dotnet run --project src/AzureSubnetPlanner.App
```

## Project structure

| Folder | Contents |
| --- | --- |
| `src/AzureSubnetPlanner.Core` | Core logic: CIDR parsing, CSV import, free block search, subnet layout (cross-platform) |
| `src/AzureSubnetPlanner.App` | WPF user interface |
| `tests/AzureSubnetPlanner.Core.Tests` | xUnit tests for the core logic |
| `installer` | WiX v5 project for the MSI |
| `kql` | Resource Graph query to export existing VNETs |
| `samples` | Sample CSV to try the app |

## How the recommendation works

All occupied ranges are merged into contiguous blocks. Within each search range the free gaps are determined, and in those gaps the first block of the requested size that is correctly aligned is selected (a /22 always starts on a multiple of 1,024 addresses). This keeps the address space compact and makes good use of small gaps between existing VNETs. Subnets are placed inside the VNET from largest to smallest, so every subnet is aligned without wasting space.
