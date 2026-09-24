using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.App.ViewModels;

public sealed record SuggestionRow(IPv4Network Network, IPv4Network Pool, bool IsRecommended)
{
    public string Prefix => Network.ToString();

    public string Range => Network.RangeText;

    public string Addresses => Network.Size.ToString("N0", PrefixOption.Dutch);

    public string PoolText => Pool.ToString();

    public string Remark => IsRecommended ? "Aanbevolen" : string.Empty;
}

public sealed record LayoutRow(SubnetAllocation Allocation)
{
    public string Name => Allocation.Name;

    public string Prefix => Allocation.Network.ToString();

    public string Range => Allocation.Network.RangeText;

    public string UsableRange => Allocation.IsFree ? string.Empty : Allocation.UsableRangeText;

    public string Usable => Allocation.IsFree ? string.Empty : Allocation.AzureUsableAddresses.ToString("N0", PrefixOption.Dutch);

    public bool IsFree => Allocation.IsFree;
}

public sealed record ReservedRow(ReservedRange Range)
{
    public string Kind => Range.Kind switch
    {
        ReservationKind.ExistingVnet => "Azure VNET",
        ReservationKind.OnPremises => "Lokaal netwerk",
        _ => "Azure gereserveerd",
    };

    public string Prefix => Range.Network.ToString();

    public string AddressRange => Range.Network.RangeText;

    public string Addresses => Range.Network.Size.ToString("N0", PrefixOption.Dutch);

    public string VnetName => Range.Vnet?.VnetName ?? string.Empty;

    public string Subscription => Range.Vnet?.SubscriptionName ?? string.Empty;

    public string SubscriptionId => Range.Vnet?.SubscriptionId ?? string.Empty;

    public string ResourceGroup => Range.Vnet?.ResourceGroup ?? string.Empty;

    public string Location => Range.Vnet?.Location ?? string.Empty;

    public string Description => Range.Vnet is null ? Range.Description : string.Empty;

    public bool Matches(string filter) =>
        new[] { Kind, Prefix, VnetName, Subscription, SubscriptionId, ResourceGroup, Location, Description }
            .Any(v => v.Contains(filter, StringComparison.OrdinalIgnoreCase));
}
