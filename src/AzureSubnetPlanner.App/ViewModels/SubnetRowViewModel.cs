namespace AzureSubnetPlanner.App.ViewModels;

/// <summary>A subnet the user wants inside the new VNET.</summary>
public sealed class SubnetRowViewModel : ObservableObject
{
    private string _name;
    private int _prefixLength;

    public SubnetRowViewModel(string name, int prefixLength)
    {
        _name = name;
        _prefixLength = prefixLength;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int PrefixLength
    {
        get => _prefixLength;
        set => SetProperty(ref _prefixLength, value);
    }
}
