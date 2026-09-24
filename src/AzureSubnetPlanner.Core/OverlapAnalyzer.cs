namespace AzureSubnetPlanner.Core;

public sealed record OverlapPair(ReservedRange First, ReservedRange Second);

public static class OverlapAnalyzer
{
    /// <summary>
    /// Finds overlapping pairs in which at least one side is an existing VNET. Prefixes of the
    /// same VNET and identical duplicate rows are ignored.
    /// </summary>
    public static IReadOnlyList<OverlapPair> FindOverlaps(IEnumerable<ReservedRange> ranges)
    {
        var sorted = ranges
            .Where(r => r.Kind != ReservationKind.AzureReserved)
            .OrderBy(r => r.Network.FirstAddress)
            .ThenBy(r => r.Network.PrefixLength)
            .ToList();
        var pairs = new List<OverlapPair>();

        for (var i = 0; i < sorted.Count; i++)
        {
            var a = sorted[i];
            for (var j = i + 1; j < sorted.Count && sorted[j].Network.FirstAddress <= a.Network.LastAddress; j++)
            {
                var b = sorted[j];
                if (a.Kind != ReservationKind.ExistingVnet && b.Kind != ReservationKind.ExistingVnet)
                {
                    continue;
                }

                if (a.Vnet is not null && b.Vnet is not null && SameVnet(a.Vnet, b.Vnet))
                {
                    continue;
                }

                pairs.Add(new OverlapPair(a, b));
            }
        }

        return pairs;
    }

    private static bool SameVnet(ExistingVnet a, ExistingVnet b) =>
        string.Equals(a.VnetName, b.VnetName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ResourceGroup, b.ResourceGroup, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.SubscriptionId, b.SubscriptionId, StringComparison.OrdinalIgnoreCase);
}
