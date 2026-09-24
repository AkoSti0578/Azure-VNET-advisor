using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class CidrListParserTests
{
    [Fact]
    public void Parse_ReadsCommentsAndMultipleEntriesPerLine()
    {
        var result = CidrListParser.Parse("192.168.0.0/16 # Kantoor\n172.16.0.0/24, 172.16.1.0/24\n\n# alleen commentaar\n");

        Assert.Empty(result.Errors);
        Assert.Equal(3, result.Entries.Count);
        Assert.Equal("Kantoor", result.Entries[0].Comment);
        Assert.Null(result.Entries[2].Comment);
    }

    [Fact]
    public void Parse_ReportsInvalidTokens()
    {
        var result = CidrListParser.Parse("10.0.0.0/8\nkantoor\nfd00::/8");

        Assert.Single(result.Entries);
        Assert.Equal(2, result.Errors.Count);
    }
}
