# Azure Subnet Planner

Windows-applicatie (WPF, .NET 8) die adviseert welk **address prefix** je kunt gebruiken voor een nieuw Azure VNET, zonder overlap met:

- **bestaande Azure VNETs** – ingelezen uit een CSV-export van Azure Resource Graph;
- **lokale / on-premises netwerken** – die je zelf invult (kantoor, datacenter, VPN);
- **door Azure gereserveerde ranges** – 127.0.0.0/8, 169.254.0.0/16, 168.63.129.16/32, 224.0.0.0/4 en 255.255.255.255/32.

Daarnaast stelt de app een **subnetindeling** voor binnen het nieuwe VNET (inclusief GatewaySubnet, AzureBastionSubnet, AzureFirewallSubnet, …) en houdt rekening met de 5 adressen die Azure per subnet reserveert.

De applicatie maakt geen verbinding met Azure of internet; alle gegevens blijven lokaal.

## Werkwijze

1. **Bestaande VNETs exporteren** – open *Resource Graph Explorer* in de Azure Portal, voer de query uit [`kql/existing-vnets.kql`](kql/existing-vnets.kql) uit (ook te kopiëren via de knop *KQL-query kopiëren* in de app) en kies *Download as CSV*.
2. **CSV importeren** – klik op *CSV importeren...*. Komma-, puntkomma- en tab-gescheiden bestanden worden ondersteund; IPv6-prefixes worden overgeslagen.
3. **Lokale netwerken invullen** – één CIDR per regel, tekst na `#` is een omschrijving:
   ```
   192.168.0.0/16  # Kantoor
   172.16.10.0/24  # Datacenter
   ```
4. **Zoekbereik** – de adresruimte waarin gezocht wordt, in volgorde van voorkeur (standaard `10.0.0.0/8`). Per zoekbereik zie je hoeveel ruimte er nog vrij is.
5. **Nieuw VNET** – kies een vaste grootte (bijv. /22) óf laat de grootte berekenen op basis van de subnets die je nodig hebt (optioneel met groeiruimte).
6. Klik op **Adviseer address prefix**. Je krijgt het aanbevolen prefix plus alternatieven, met per voorstel de subnetindeling. Die kun je kopiëren, als CSV exporteren of als Azure CLI-commando's kopiëren.

Verder:

- **Prefix controleren** – controleer een zelfgekozen prefix en zie precies met welke VNETs/netwerken het overlapt.
- **Bezette adresruimte** – overzicht (met filter) van alle bezette ranges.
- **Meldingen** – importwaarschuwingen en bestaande overlappingen tussen VNETs en/of lokale netwerken.

De ingevulde lokale netwerken, het zoekbereik en het laatst gebruikte CSV-bestand worden bewaard in `%APPDATA%\AzureSubnetPlanner\settings.json`.

## MSI bouwen

### Via GitHub Actions

De workflow [`.github/workflows/build-msi.yml`](.github/workflows/build-msi.yml) draait bij elke push op `windows-latest`, voert de tests uit en publiceert de MSI als build-artifact (*Actions → Build MSI → Artifacts*). Push een tag zoals `v1.2.0` om een GitHub Release met de MSI te maken.

### Lokaal (Windows)

Vereist: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). WiX Toolset wordt automatisch via NuGet opgehaald.

```powershell
./build.ps1 -Version 1.0.0
```

Resultaat: `artifacts\msi\AzureSubnetPlanner.msi`. De app wordt self-contained gepubliceerd, dus op de doelcomputer is geen .NET-runtime nodig. De MSI installeert per machine in `C:\Program Files\Azure Subnet Planner` met snelkoppelingen in het Startmenu en op het bureaublad; een nieuwere versie vervangt automatisch de oude.

Alleen de app starten tijdens ontwikkeling:

```powershell
dotnet run --project src/AzureSubnetPlanner.App
```

## Projectstructuur

| Map | Inhoud |
| --- | --- |
| `src/AzureSubnetPlanner.Core` | Rekenlogica: CIDR-parsing, CSV-import, zoeken naar vrije blokken, subnetindeling (platformonafhankelijk) |
| `src/AzureSubnetPlanner.App` | WPF-gebruikersinterface |
| `tests/AzureSubnetPlanner.Core.Tests` | xUnit-tests voor de rekenlogica |
| `installer` | WiX v5-project voor de MSI |
| `kql` | Resource Graph-query voor de export van bestaande VNETs |
| `samples` | Voorbeeld-CSV om de app te proberen |

## Hoe het advies werkt

Alle bezette ranges worden samengevoegd tot aaneengesloten blokken. Binnen elk zoekbereik worden de vrije gaten bepaald, en daarin het eerste blok van de gevraagde grootte dat correct is uitgelijnd (een /22 begint altijd op een veelvoud van 1.024 adressen). Zo blijft de adresruimte compact en worden kleine gaten tussen bestaande VNETs zinvol benut. Subnets worden binnen het VNET van groot naar klein geplaatst, zodat elk subnet uitgelijnd is zonder verspilling.
