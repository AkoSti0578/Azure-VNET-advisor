using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class SubnetLayoutPlannerTests
{
    [Theory]
    [InlineData(1, 29)]
    [InlineData(3, 29)]
    [InlineData(4, 28)]
    [InlineData(59, 26)]
    [InlineData(60, 25)]
    [InlineData(251, 24)]
    public void PrefixForUsableHosts_AccountsForAzureReservedAddresses(int hosts, int expected) =>
        Assert.Equal(expected, SubnetLayoutPlanner.PrefixForUsableHosts(hosts));

    [Fact]
    public void RequiredVnetPrefix_RoundsUpToPowerOfTwo()
    {
        var requests = new[]
        {
            new SubnetRequest("app", 24),
            new SubnetRequest("GatewaySubnet", 27),
            new SubnetRequest("AzureBastionSubnet", 26),
        };

        Assert.Equal(23, SubnetLayoutPlanner.RequiredVnetPrefix(requests));
    }

    [Fact]
    public void Layout_PlacesLargestFirstAndReportsFreeSpace()
    {
        var layout = SubnetLayoutPlanner.Layout(
            IPv4Network.Parse("10.10.0.0/23"),
            [new SubnetRequest("GatewaySubnet", 27), new SubnetRequest("app", 24), new SubnetRequest("AzureBastionSubnet", 26)]);

        Assert.Equal(
            ["app 10.10.0.0/24", "AzureBastionSubnet 10.10.1.0/26", "GatewaySubnet 10.10.1.64/27", "(free) 10.10.1.96/27", "(free) 10.10.1.128/25"],
            layout.Select(a => $"{a.Name} {a.Network}"));
        Assert.Equal(251, layout[0].AzureUsableAddresses);
        Assert.Equal("10.10.0.4 - 10.10.0.254", layout[0].UsableRangeText);
    }

    [Fact]
    public void Layout_ThrowsWhenSubnetsDoNotFit() =>
        Assert.Throws<InvalidOperationException>(() => SubnetLayoutPlanner.Layout(
            IPv4Network.Parse("10.0.0.0/24"),
            [new SubnetRequest("a", 25), new SubnetRequest("b", 25), new SubnetRequest("c", 29)]));
}
