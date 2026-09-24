using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class IPv4NetworkTests
{
    [Theory]
    [InlineData("10.0.0.0/16", "10.0.0.0/16")]
    [InlineData(" 10.1.2.3/16 ", "10.1.0.0/16")]
    [InlineData("192.168.1.10", "192.168.1.10/32")]
    [InlineData("0.0.0.0/0", "0.0.0.0/0")]
    public void Parse_NormalizesToNetworkAddress(string input, string expected) =>
        Assert.Equal(expected, IPv4Network.Parse(input).ToString());

    [Theory]
    [InlineData("10.0.0/16")]
    [InlineData("10.0.0.256/16")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/")]
    [InlineData("10")]
    [InlineData("fd00::/8")]
    [InlineData("")]
    public void TryParse_RejectsInvalidInput(string input) =>
        Assert.False(IPv4Network.TryParse(input, out _));

    [Fact]
    public void Overlaps_DetectsNestedAndAdjacentRanges()
    {
        var big = IPv4Network.Parse("10.0.0.0/16");
        Assert.True(big.Overlaps(IPv4Network.Parse("10.0.5.0/24")));
        Assert.True(IPv4Network.Parse("10.0.5.0/24").Overlaps(big));
        Assert.False(big.Overlaps(IPv4Network.Parse("10.1.0.0/16")));
        Assert.True(big.Contains(IPv4Network.Parse("10.0.255.0/24")));
    }

    [Fact]
    public void RangeText_IsInclusive() =>
        Assert.Equal("10.0.0.0 - 10.0.3.255", IPv4Network.Parse("10.0.0.0/22").RangeText);
}
