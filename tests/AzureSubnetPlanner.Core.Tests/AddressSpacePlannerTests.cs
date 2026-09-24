using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class AddressSpacePlannerTests
{
    private static ReservedRange Vnet(string cidr) =>
        new(IPv4Network.Parse(cidr), ReservationKind.ExistingVnet, cidr);

    private static IPv4Network N(string cidr) => IPv4Network.Parse(cidr);

    [Fact]
    public void FindFreeBlocks_SkipsExistingVnetsAndAligns()
    {
        var planner = new AddressSpacePlanner([Vnet("10.0.0.0/16"), Vnet("10.1.0.0/24")]);

        var blocks = planner.FindFreeBlocks([N("10.0.0.0/8")], 16, 3);

        Assert.Equal(["10.2.0.0/16", "10.3.0.0/16", "10.4.0.0/16"], blocks.Select(b => b.ToString()));
    }

    [Fact]
    public void FindFreeBlocks_UsesGapsBetweenReservations()
    {
        var planner = new AddressSpacePlanner([Vnet("10.0.0.0/24"), Vnet("10.0.2.0/24")]);

        var blocks = planner.FindFreeBlocks([N("10.0.0.0/16")], 24, 2);

        Assert.Equal(["10.0.1.0/24", "10.0.3.0/24"], blocks.Select(b => b.ToString()));
    }

    [Fact]
    public void FindFreeBlocks_RespectsOnPremisesRanges()
    {
        var planner = new AddressSpacePlanner(
        [
            new ReservedRange(N("10.0.0.0/12"), ReservationKind.OnPremises, "kantoor"),
        ]);

        var block = Assert.Single(planner.FindFreeBlocks([N("10.0.0.0/8")], 20, 1));

        Assert.Equal("10.16.0.0/20", block.ToString());
    }

    [Fact]
    public void FindFreeBlocks_ReturnsNothingWhenPoolIsFull()
    {
        var planner = new AddressSpacePlanner([Vnet("192.168.0.0/16")]);

        Assert.Empty(planner.FindFreeBlocks([N("192.168.0.0/16")], 24, 5));
    }

    [Fact]
    public void FindFreeBlocks_SearchesPoolsInOrderAndSkipsTooSmallPools()
    {
        var planner = new AddressSpacePlanner([]);

        var blocks = planner.FindFreeBlocks([N("192.168.0.0/16"), N("172.16.0.0/12")], 14, 2);

        Assert.Equal(["172.16.0.0/14", "172.20.0.0/14"], blocks.Select(b => b.ToString()));
    }

    [Fact]
    public void FindFreeBlocks_HandlesTopOfAddressSpace()
    {
        var planner = new AddressSpacePlanner(AzureReservedRanges.All);

        Assert.Empty(planner.FindFreeBlocks([N("255.255.255.0/24")], 24, 5));
        var blocks = planner.FindFreeBlocks([N("255.255.255.0/24")], 25, 5);

        Assert.Equal(["255.255.255.0/25"], blocks.Select(b => b.ToString()));
    }

    [Fact]
    public void FindConflicts_ReturnsAllOverlappingReservations()
    {
        var planner = new AddressSpacePlanner([Vnet("10.0.0.0/24"), Vnet("10.0.1.0/24"), Vnet("10.5.0.0/16")]);

        var conflicts = planner.FindConflicts(N("10.0.0.0/23"));

        Assert.Equal(["10.0.0.0/24", "10.0.1.0/24"], conflicts.Select(c => c.Network.ToString()));
        Assert.True(planner.IsFree(N("10.0.2.0/23")));
    }

    [Fact]
    public void GetFreeSpace_ReportsFreeAddressesAndLargestBlock()
    {
        var planner = new AddressSpacePlanner([Vnet("10.0.0.0/17")]);

        var free = planner.GetFreeSpace(N("10.0.0.0/16"));

        Assert.Equal(32768UL, free.FreeAddresses);
        Assert.Equal(17, free.LargestFreePrefix);
        Assert.Equal(50.0, free.FreePercentage, 3);
    }

    [Fact]
    public void ToCidrBlocks_SplitsUnalignedRange()
    {
        var blocks = AddressSpacePlanner.ToCidrBlocks(N("10.0.0.64/32").Address, N("10.0.0.255/32").Address);

        Assert.Equal(["10.0.0.64/26", "10.0.0.128/25"], blocks.Select(b => b.ToString()));
    }
}
