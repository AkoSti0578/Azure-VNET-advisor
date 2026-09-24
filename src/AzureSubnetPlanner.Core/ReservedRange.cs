namespace AzureSubnetPlanner.Core;

public enum ReservationKind
{
    ExistingVnet,
    OnPremises,
    AzureReserved,
}

/// <summary>An address range that a new VNET must not overlap with.</summary>
public sealed record ReservedRange(IPv4Network Network, ReservationKind Kind, string Description, ExistingVnet? Vnet = null)
{
    public static ReservedRange FromVnet(ExistingVnet vnet) =>
        new(vnet.AddressPrefix, ReservationKind.ExistingVnet, DescribeVnet(vnet), vnet);

    private static string DescribeVnet(ExistingVnet vnet)
    {
        var details = new[] { vnet.SubscriptionName, vnet.ResourceGroup, vnet.Location }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var name = string.IsNullOrWhiteSpace(vnet.VnetName) ? "(onbekend VNET)" : vnet.VnetName;
        var suffix = string.Join(" / ", details);
        return suffix.Length == 0 ? name : $"{name} ({suffix})";
    }
}

/// <summary>Ranges Azure does not allow in a VNET address space.</summary>
public static class AzureReservedRanges
{
    public static IReadOnlyList<ReservedRange> All { get; } =
    [
        new(IPv4Network.Parse("127.0.0.0/8"), ReservationKind.AzureReserved, "Loopback (niet toegestaan in Azure)"),
        new(IPv4Network.Parse("169.254.0.0/16"), ReservationKind.AzureReserved, "Link-local (niet toegestaan in Azure)"),
        new(IPv4Network.Parse("168.63.129.16/32"), ReservationKind.AzureReserved, "Azure interne DNS / health probe"),
        new(IPv4Network.Parse("224.0.0.0/4"), ReservationKind.AzureReserved, "Multicast (niet toegestaan in Azure)"),
        new(IPv4Network.Parse("255.255.255.255/32"), ReservationKind.AzureReserved, "Broadcast (niet toegestaan in Azure)"),
    ];
}
