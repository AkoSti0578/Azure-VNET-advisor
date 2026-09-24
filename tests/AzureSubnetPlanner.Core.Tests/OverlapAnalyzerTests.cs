using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class OverlapAnalyzerTests
{
    private static ReservedRange Vnet(string name, string cidr) =>
        ReservedRange.FromVnet(new ExistingVnet("sub", "id", "rg", name, "westeurope", IPv4Network.Parse(cidr)));

    [Fact]
    public void FindOverlaps_ReportsVnetConflictsButNotSameVnet()
    {
        var ranges = new[]
        {
            Vnet("a", "10.0.0.0/16"),
            Vnet("a", "10.0.0.0/16"),
            Vnet("b", "10.0.4.0/24"),
            new ReservedRange(IPv4Network.Parse("10.0.8.0/24"), ReservationKind.OnPremises, "office"),
            new ReservedRange(IPv4Network.Parse("10.0.8.0/25"), ReservationKind.OnPremises, "office 2"),
        };

        var pairs = OverlapAnalyzer.FindOverlaps(ranges);

        // a overlaps b (x2 because of the duplicate row) and the on-premises range; on-prem vs on-prem is ignored.
        Assert.Equal(6, pairs.Count);
        Assert.DoesNotContain(pairs, p => p.First.Kind == ReservationKind.OnPremises && p.Second.Kind == ReservationKind.OnPremises);
    }
}
