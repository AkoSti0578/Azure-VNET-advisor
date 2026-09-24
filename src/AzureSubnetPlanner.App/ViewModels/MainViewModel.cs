using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using AzureSubnetPlanner.App.Services;
using AzureSubnetPlanner.Core;
using Microsoft.Win32;

namespace AzureSubnetPlanner.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxOverlapWarnings = 200;

    private static readonly IPv4Network[] PrivateRanges =
    [
        IPv4Network.Parse("10.0.0.0/8"),
        IPv4Network.Parse("172.16.0.0/12"),
        IPv4Network.Parse("192.168.0.0/16"),
        IPv4Network.Parse("100.64.0.0/10"),
    ];

    private readonly SettingsStore _settingsStore = new();

    private List<ExistingVnet> _vnets = [];
    private List<string> _csvWarnings = [];
    private string? _csvPath;
    private IReadOnlyList<CidrEntry> _localNetworks = [];
    private IReadOnlyList<IPv4Network> _pools = [];
    private AddressSpacePlanner _planner = new(AzureReservedRanges.All);
    private IReadOnlyList<SubnetRequest> _adviceSubnets = [];

    private string _csvStatus = "Nog geen CSV geladen.";
    private string _localNetworksText = string.Empty;
    private string _localNetworksError = string.Empty;
    private string _searchPoolsText = string.Empty;
    private string _poolsError = string.Empty;
    private string _poolInfo = string.Empty;
    private string _vnetName = "vnet-nieuw";
    private bool _useSubnetSize = true;
    private int _selectedVnetPrefix = 22;
    private bool _addGrowthSpace;
    private SubnetRowViewModel? _selectedSubnet;
    private int _suggestionCount = 5;
    private SuggestionRow? _selectedSuggestion;
    private string? _recommendedPrefix;
    private string _adviceText = "Vul links de gegevens in en klik op 'Adviseer address prefix'.";
    private bool _adviceIsError;
    private string _layoutTitle = "Subnetindeling";
    private string _checkPrefixText = string.Empty;
    private string _checkResultText = string.Empty;
    private string _checkState = string.Empty;
    private string _reservedFilter = string.Empty;
    private string _statusMessage = "Gereed.";
    private int _selectedTabIndex;

    public MainViewModel()
    {
        ReservedRowsView = CollectionViewSource.GetDefaultView(ReservedRows);
        ReservedRowsView.Filter = o => string.IsNullOrWhiteSpace(ReservedFilter) || ((ReservedRow)o).Matches(ReservedFilter.Trim());

        ImportCsvCommand = new RelayCommand(ImportCsv);
        ClearCsvCommand = new RelayCommand(ClearCsv, () => _vnets.Count > 0 || _csvPath is not null);
        CopyKqlCommand = new RelayCommand(() => CopyToClipboard(KqlQueries.ExistingVnets, "KQL-query gekopieerd naar het klembord."));
        AddSubnetCommand = new RelayCommand(AddSubnet);
        RemoveSubnetCommand = new RelayCommand(RemoveSubnet, () => SelectedSubnet is not null);
        AddPresetCommand = new RelayCommand(AddPreset);
        AdviseCommand = new RelayCommand(Advise);
        CopyCommand = new RelayCommand(p => CopyToClipboard(p as string, $"'{p}' gekopieerd naar het klembord."), p => !string.IsNullOrEmpty(p as string));
        CopyLayoutCommand = new RelayCommand(CopyLayout, () => SelectedSuggestion is not null);
        CopyCliCommand = new RelayCommand(CopyAzureCli, () => SelectedSuggestion is not null);
        ExportLayoutCommand = new RelayCommand(ExportLayout, () => SelectedSuggestion is not null);
        CheckPrefixCommand = new RelayCommand(CheckPrefix);
        CheckSuggestionCommand = new RelayCommand(CheckSelectedSuggestion, () => SelectedSuggestion is not null);

        var settings = _settingsStore.Load();
        _localNetworksText = settings.LocalNetworksText ?? "# Eén CIDR per regel, tekst na # is een omschrijving\n# 192.168.0.0/16 # Kantoor\n";
        _searchPoolsText = settings.SearchPoolsText ?? "10.0.0.0/8\n";
        _vnetName = string.IsNullOrWhiteSpace(settings.VnetName) ? _vnetName : settings.VnetName;

        Subnets.Add(new SubnetRowViewModel("snet-workload", 24));

        ParseLocalNetworks();
        ParsePools();
        if (!string.IsNullOrEmpty(settings.LastCsvPath) && File.Exists(settings.LastCsvPath))
        {
            LoadCsv(settings.LastCsvPath, showErrors: false);
        }
        else
        {
            Rebuild();
        }

        AdviceText = "Vul links de gegevens in en klik op 'Adviseer address prefix'.";
    }

    public ICommand ImportCsvCommand { get; }

    public ICommand ClearCsvCommand { get; }

    public ICommand CopyKqlCommand { get; }

    public ICommand AddSubnetCommand { get; }

    public ICommand RemoveSubnetCommand { get; }

    public ICommand AddPresetCommand { get; }

    public ICommand AdviseCommand { get; }

    public ICommand CopyCommand { get; }

    public ICommand CopyLayoutCommand { get; }

    public ICommand CopyCliCommand { get; }

    public ICommand ExportLayoutCommand { get; }

    public ICommand CheckPrefixCommand { get; }

    public ICommand CheckSuggestionCommand { get; }

    public IReadOnlyList<PrefixOption> VnetPrefixOptions => PrefixOption.VnetOptions;

    public IReadOnlyList<int> SuggestionCountOptions { get; } = [1, 3, 5, 10, 25, 50];

    public ObservableCollection<SubnetRowViewModel> Subnets { get; } = [];

    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];

    public ObservableCollection<LayoutRow> LayoutRows { get; } = [];

    public ObservableCollection<ReservedRow> ReservedRows { get; } = [];

    public ICollectionView ReservedRowsView { get; }

    public ObservableCollection<ReservedRow> CheckConflicts { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public string CsvStatus
    {
        get => _csvStatus;
        private set => SetProperty(ref _csvStatus, value);
    }

    public string LocalNetworksText
    {
        get => _localNetworksText;
        set
        {
            if (SetProperty(ref _localNetworksText, value))
            {
                ParseLocalNetworks();
                Rebuild();
            }
        }
    }

    public string LocalNetworksError
    {
        get => _localNetworksError;
        private set => SetProperty(ref _localNetworksError, value);
    }

    public string SearchPoolsText
    {
        get => _searchPoolsText;
        set
        {
            if (SetProperty(ref _searchPoolsText, value))
            {
                ParsePools();
                Rebuild();
            }
        }
    }

    public string PoolsError
    {
        get => _poolsError;
        private set => SetProperty(ref _poolsError, value);
    }

    public string PoolInfo
    {
        get => _poolInfo;
        private set => SetProperty(ref _poolInfo, value);
    }

    public string VnetName
    {
        get => _vnetName;
        set => SetProperty(ref _vnetName, value);
    }

    public bool UseSubnetSize
    {
        get => _useSubnetSize;
        set
        {
            if (SetProperty(ref _useSubnetSize, value))
            {
                OnPropertyChanged(nameof(UseFixedSize));
            }
        }
    }

    public bool UseFixedSize
    {
        get => !_useSubnetSize;
        set => UseSubnetSize = !value;
    }

    public int SelectedVnetPrefix
    {
        get => _selectedVnetPrefix;
        set => SetProperty(ref _selectedVnetPrefix, value);
    }

    public bool AddGrowthSpace
    {
        get => _addGrowthSpace;
        set => SetProperty(ref _addGrowthSpace, value);
    }

    public SubnetRowViewModel? SelectedSubnet
    {
        get => _selectedSubnet;
        set => SetProperty(ref _selectedSubnet, value);
    }

    public int SuggestionCount
    {
        get => _suggestionCount;
        set => SetProperty(ref _suggestionCount, value);
    }

    public SuggestionRow? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set
        {
            if (SetProperty(ref _selectedSuggestion, value))
            {
                UpdateLayout();
            }
        }
    }

    public string? RecommendedPrefix
    {
        get => _recommendedPrefix;
        private set
        {
            if (SetProperty(ref _recommendedPrefix, value))
            {
                OnPropertyChanged(nameof(HasRecommendation));
            }
        }
    }

    public bool HasRecommendation => !string.IsNullOrEmpty(RecommendedPrefix);

    public string AdviceText
    {
        get => _adviceText;
        private set => SetProperty(ref _adviceText, value);
    }

    public bool AdviceIsError
    {
        get => _adviceIsError;
        private set => SetProperty(ref _adviceIsError, value);
    }

    public string LayoutTitle
    {
        get => _layoutTitle;
        private set => SetProperty(ref _layoutTitle, value);
    }

    public string CheckPrefixText
    {
        get => _checkPrefixText;
        set => SetProperty(ref _checkPrefixText, value);
    }

    public string CheckResultText
    {
        get => _checkResultText;
        private set => SetProperty(ref _checkResultText, value);
    }

    /// <summary>"free", "conflict", "invalid" or empty; drives the colour of the result.</summary>
    public string CheckState
    {
        get => _checkState;
        private set => SetProperty(ref _checkState, value);
    }

    public string ReservedFilter
    {
        get => _reservedFilter;
        set
        {
            if (SetProperty(ref _reservedFilter, value))
            {
                ReservedRowsView.Refresh();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public void SaveSettings() =>
        _settingsStore.Save(new AppSettings
        {
            LocalNetworksText = LocalNetworksText,
            SearchPoolsText = SearchPoolsText,
            LastCsvPath = _csvPath,
            VnetName = VnetName,
        });

    private void ImportCsv()
    {
        var dialog = new OpenFileDialog
        {
            Title = "CSV met bestaande VNETs importeren",
            Filter = "CSV-bestanden (*.csv)|*.csv|Alle bestanden (*.*)|*.*",
        };
        if (_csvPath is not null && Directory.Exists(Path.GetDirectoryName(_csvPath)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_csvPath);
        }

        if (dialog.ShowDialog() == true)
        {
            LoadCsv(dialog.FileName, showErrors: true);
        }
    }

    private void LoadCsv(string path, bool showErrors)
    {
        try
        {
            var result = VnetCsvReader.ReadFile(path);
            _vnets = result.Vnets;
            _csvWarnings = result.Warnings;
            _csvPath = path;

            var vnetCount = _vnets
                .Select(v => (v.SubscriptionId.ToUpperInvariant(), v.ResourceGroup.ToUpperInvariant(), v.VnetName.ToUpperInvariant()))
                .Distinct()
                .Count();
            var status = $"{Path.GetFileName(path)}: {vnetCount} VNET(s) met {_vnets.Count} address prefix(es) geladen.";
            if (result.Ipv6PrefixesSkipped > 0)
            {
                status += $" {result.Ipv6PrefixesSkipped} IPv6-prefix(es) overgeslagen.";
            }

            if (result.Warnings.Count > 0)
            {
                status += $" {result.Warnings.Count} melding(en), zie tabblad Meldingen.";
            }

            CsvStatus = status;
            StatusMessage = status;
            Rebuild();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (showErrors)
            {
                MessageBox.Show($"Het CSV-bestand kon niet worden gelezen:\n\n{ex.Message}", "CSV importeren",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            StatusMessage = $"CSV niet geladen: {ex.Message}";
            Rebuild();
        }
    }

    private void ClearCsv()
    {
        _vnets = [];
        _csvWarnings = [];
        _csvPath = null;
        CsvStatus = "Nog geen CSV geladen.";
        StatusMessage = "Bestaande VNETs gewist.";
        Rebuild();
    }

    private void ParseLocalNetworks()
    {
        var result = CidrListParser.Parse(LocalNetworksText);
        _localNetworks = result.Entries;
        LocalNetworksError = string.Join(Environment.NewLine, result.Errors);
    }

    private void ParsePools()
    {
        var result = CidrListParser.Parse(SearchPoolsText);
        _pools = result.Entries.Select(e => e.Network).Distinct().ToList();
        var errors = result.Errors.ToList();
        if (_pools.Count == 0 && errors.Count == 0)
        {
            errors.Add("Vul minimaal één zoekbereik in, bijvoorbeeld 10.0.0.0/8.");
        }

        PoolsError = string.Join(Environment.NewLine, errors);
    }

    /// <summary>Recreates the planner after any change to the existing or local networks.</summary>
    private void Rebuild()
    {
        var reserved = new List<ReservedRange>();
        reserved.AddRange(_vnets.Select(ReservedRange.FromVnet));
        reserved.AddRange(_localNetworks.Select(e => new ReservedRange(
            e.Network,
            ReservationKind.OnPremises,
            e.Comment is null ? "Lokaal netwerk" : $"Lokaal: {e.Comment}")));
        reserved.AddRange(AzureReservedRanges.All);
        _planner = new AddressSpacePlanner(reserved);

        ReservedRows.Clear();
        foreach (var range in reserved.OrderBy(r => r.Network).ThenBy(r => r.Description))
        {
            ReservedRows.Add(new ReservedRow(range));
        }

        UpdatePoolInfo();
        UpdateWarnings();
        ClearAdvice("De invoer is gewijzigd. Klik op 'Adviseer address prefix' voor een nieuw advies.");
    }

    private void UpdatePoolInfo()
    {
        var lines = _pools.Select(pool =>
        {
            var free = _planner.GetFreeSpace(pool);
            var largest = free.LargestFreePrefix is { } p ? $"grootste vrije blok /{p}" : "geen vrije ruimte";
            return $"{pool}: {free.FreePercentage.ToString("0.#", PrefixOption.Dutch)}% vrij, {largest}";
        });
        PoolInfo = string.Join(Environment.NewLine, lines);
    }

    private void UpdateWarnings()
    {
        Warnings.Clear();
        foreach (var warning in _csvWarnings)
        {
            Warnings.Add(warning);
        }

        foreach (var pool in _pools.Where(pool => !PrivateRanges.Any(r => r.Contains(pool))))
        {
            Warnings.Add($"Zoekbereik {pool} valt (deels) buiten de privé-adresruimte (RFC 1918 / 100.64.0.0/10).");
        }

        var overlaps = OverlapAnalyzer.FindOverlaps(_planner.Reserved);
        foreach (var pair in overlaps.Take(MaxOverlapWarnings))
        {
            Warnings.Add($"Overlap: {pair.First.Network} {Describe(pair.First)}  ↔  {pair.Second.Network} {Describe(pair.Second)}");
        }

        if (overlaps.Count > MaxOverlapWarnings)
        {
            Warnings.Add($"... en nog {overlaps.Count - MaxOverlapWarnings} overlap(pen).");
        }
    }

    private static string Describe(ReservedRange range) =>
        range.Kind == ReservationKind.ExistingVnet ? $"(VNET {range.Description})" : $"({range.Description})";

    private void AddSubnet()
    {
        var row = new SubnetRowViewModel($"snet-{Subnets.Count + 1}", 26);
        Subnets.Add(row);
        SelectedSubnet = row;
    }

    private void RemoveSubnet()
    {
        if (SelectedSubnet is not null)
        {
            Subnets.Remove(SelectedSubnet);
        }
    }

    /// <summary>Parameter format: "Name|prefixLength", e.g. "GatewaySubnet|27".</summary>
    private void AddPreset(object? parameter)
    {
        if (parameter is not string text || text.Split('|') is not [var name, var prefixText]
            || !int.TryParse(prefixText, out var prefix))
        {
            return;
        }

        if (Subnets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"{name} staat al in de lijst.";
            return;
        }

        var row = new SubnetRowViewModel(name, prefix);
        Subnets.Add(row);
        SelectedSubnet = row;
    }

    private void Advise()
    {
        SelectedTabIndex = 0;
        ClearAdvice(null);

        if (!string.IsNullOrEmpty(LocalNetworksError))
        {
            ShowAdviceError("Corrigeer eerst de fouten bij 'Lokale netwerken'.");
            return;
        }

        if (!string.IsNullOrEmpty(PoolsError))
        {
            ShowAdviceError("Corrigeer eerst de fouten bij 'Zoekbereik'.");
            return;
        }

        var requests = Subnets
            .Select(s => new SubnetRequest(string.IsNullOrWhiteSpace(s.Name) ? "(naamloos)" : s.Name.Trim(), s.PrefixLength))
            .ToList();

        int prefix;
        if (UseSubnetSize)
        {
            if (requests.Count == 0)
            {
                ShowAdviceError("Voeg minimaal één subnet toe, of kies een vaste VNET-grootte.");
                return;
            }

            prefix = SubnetLayoutPlanner.RequiredVnetPrefix(requests);
            if (AddGrowthSpace)
            {
                prefix = Math.Max(1, prefix - 1);
            }
        }
        else
        {
            prefix = SelectedVnetPrefix;
            if (requests.Count > 0)
            {
                var required = SubnetLayoutPlanner.RequiredVnetPrefix(requests);
                if (required < prefix)
                {
                    ShowAdviceError($"De opgegeven subnets passen niet in een /{prefix}; daarvoor is minimaal een /{required} nodig.");
                    return;
                }
            }
        }

        var blocks = _planner.FindFreeBlocks(_pools, prefix, SuggestionCount);
        if (blocks.Count == 0)
        {
            ShowAdviceError($"Geen vrij /{prefix}-blok gevonden in het zoekbereik ({string.Join(", ", _pools)}). " +
                            "Vergroot het zoekbereik of kies een kleiner VNET.");
            return;
        }

        _adviceSubnets = requests;
        for (var i = 0; i < blocks.Count; i++)
        {
            var pool = _pools.First(p => p.Contains(blocks[i]));
            Suggestions.Add(new SuggestionRow(blocks[i], pool, IsRecommended: i == 0));
        }

        var vnetPrefixCount = _vnets.Count;
        var localCount = _localNetworks.Count;
        RecommendedPrefix = blocks[0].ToString();
        AdviceText =
            $"Vrij /{prefix}-blok ({blocks[0].Size.ToString("N0", PrefixOption.Dutch)} adressen, {blocks[0].RangeText}). " +
            $"Gecontroleerd tegen {vnetPrefixCount} bestaande VNET-prefix(es), {localCount} lokale netwerk(en) " +
            "en de door Azure gereserveerde ranges." +
            (blocks.Count > 1 ? $" Hieronder staan ook {blocks.Count - 1} alternatieve prefix(es)." : string.Empty);
        SelectedSuggestion = Suggestions[0];
        StatusMessage = $"Advies: gebruik {RecommendedPrefix} als address prefix voor {VnetName}.";
    }

    private void ShowAdviceError(string message)
    {
        AdviceIsError = true;
        AdviceText = message;
        StatusMessage = message;
    }

    private void ClearAdvice(string? message)
    {
        Suggestions.Clear();
        SelectedSuggestion = null;
        RecommendedPrefix = null;
        AdviceIsError = false;
        if (message is not null)
        {
            AdviceText = message;
        }
    }

    private void UpdateLayout()
    {
        LayoutRows.Clear();
        if (SelectedSuggestion is null)
        {
            LayoutTitle = "Subnetindeling";
            return;
        }

        LayoutTitle = $"Subnetindeling voor {SelectedSuggestion.Prefix}";
        try
        {
            foreach (var allocation in SubnetLayoutPlanner.Layout(SelectedSuggestion.Network, _adviceSubnets))
            {
                LayoutRows.Add(new LayoutRow(allocation));
            }
        }
        catch (InvalidOperationException ex)
        {
            LayoutTitle = ex.Message;
        }
    }

    private void CheckPrefix()
    {
        CheckConflicts.Clear();
        var input = CheckPrefixText.Trim();
        if (!input.Contains('/') || !IPv4Network.TryParse(input, out var network))
        {
            CheckState = "invalid";
            CheckResultText = "Voer een geldig IPv4 address prefix in, bijvoorbeeld 10.20.0.0/16.";
            return;
        }

        var text = new StringBuilder();
        if (!string.Equals(network.ToString(), input, StringComparison.Ordinal))
        {
            text.Append($"Let op: '{input}' is geen netwerkadres; Azure verwacht {network}. ");
        }

        var conflicts = _planner.FindConflicts(network);
        foreach (var conflict in conflicts)
        {
            CheckConflicts.Add(new ReservedRow(conflict));
        }

        if (conflicts.Count == 0)
        {
            CheckState = "free";
            text.Append($"{network} ({network.RangeText}) is vrij: geen overlap met bestaande VNETs of lokale netwerken.");
            if (!_pools.Any(p => p.Contains(network)))
            {
                text.Append(" Het prefix valt wel buiten het opgegeven zoekbereik.");
            }
        }
        else
        {
            CheckState = "conflict";
            text.Append($"{network} ({network.RangeText}) overlapt met {conflicts.Count} netwerk(en):");
        }

        CheckResultText = text.ToString();
        StatusMessage = $"Controle {network}: {(conflicts.Count == 0 ? "vrij" : $"{conflicts.Count} conflict(en)")}.";
    }

    private void CheckSelectedSuggestion()
    {
        if (SelectedSuggestion is null)
        {
            return;
        }

        CheckPrefixText = SelectedSuggestion.Prefix;
        CheckPrefix();
        SelectedTabIndex = 1;
    }

    private void CopyLayout()
    {
        if (SelectedSuggestion is null)
        {
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"VNET\t{VnetName}\t{SelectedSuggestion.Prefix}\t{SelectedSuggestion.Range}");
        foreach (var row in LayoutRows)
        {
            text.AppendLine($"{row.Name}\t{row.Prefix}\t{row.Range}\t{row.Usable}");
        }

        CopyToClipboard(text.ToString(), "Subnetindeling gekopieerd (tab-gescheiden, te plakken in Excel).");
    }

    private void CopyAzureCli()
    {
        if (SelectedSuggestion is null)
        {
            return;
        }

        var vnet = string.IsNullOrWhiteSpace(VnetName) ? "vnet-nieuw" : VnetName.Trim();
        var text = new StringBuilder();
        text.AppendLine("# Vul <resource-group> en <location> in voordat je de commando's uitvoert.");
        text.AppendLine($"az network vnet create --resource-group <resource-group> --location <location> --name {vnet} --address-prefixes {SelectedSuggestion.Prefix}");
        foreach (var row in LayoutRows.Where(r => !r.IsFree))
        {
            text.AppendLine($"az network vnet subnet create --resource-group <resource-group> --vnet-name {vnet} --name {row.Name} --address-prefixes {row.Prefix}");
        }

        CopyToClipboard(text.ToString(), "Azure CLI-commando's gekopieerd naar het klembord.");
    }

    private void ExportLayout()
    {
        if (SelectedSuggestion is null)
        {
            return;
        }

        var vnet = string.IsNullOrWhiteSpace(VnetName) ? "vnet-nieuw" : VnetName.Trim();
        var dialog = new SaveFileDialog
        {
            Title = "Subnetindeling exporteren",
            Filter = "CSV-bestanden (*.csv)|*.csv",
            FileName = $"{vnet}-{SelectedSuggestion.Prefix.Replace('/', '_')}.csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var csv = new StringBuilder();
        csv.AppendLine("vnetName;vnetAddressPrefix;subnetName;subnetAddressPrefix;range;usableRange;usableAddresses");
        foreach (var row in LayoutRows)
        {
            csv.AppendLine(string.Join(';', vnet, SelectedSuggestion.Prefix, row.Name, row.Prefix, row.Range, row.UsableRange,
                row.IsFree ? string.Empty : row.Allocation.AzureUsableAddresses.ToString()));
        }

        try
        {
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusMessage = $"Subnetindeling opgeslagen in {dialog.FileName}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Opslaan mislukt:\n\n{ex.Message}", "Exporteren", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyToClipboard(string? text, string message)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            StatusMessage = message;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusMessage = "Het klembord is bezet door een ander programma; probeer het opnieuw.";
        }
    }
}
