using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.Core.Tests;

public class VnetCsvReaderTests
{
    [Fact]
    public void Read_ParsesResourceGraphExport()
    {
        const string csv = "﻿\"subscriptionName\",\"subscriptionId\",\"resourceGroup\",\"vnetName\",\"location\",\"addressPrefix\"\r\n"
            + "\"Prod\",\"1111\",\"rg-net\",\"vnet-hub\",\"westeurope\",\"10.0.0.0/16\"\r\n"
            + "\"Prod\",\"1111\",\"rg-net\",\"vnet-spoke\",\"westeurope\",\"10.1.0.0/22\"\r\n";

        var result = VnetCsvReader.Read(csv);

        Assert.Equal(2, result.Vnets.Count);
        Assert.Empty(result.Warnings);
        var hub = result.Vnets[0];
        Assert.Equal(("Prod", "1111", "rg-net", "vnet-hub", "westeurope", "10.0.0.0/16"),
            (hub.SubscriptionName, hub.SubscriptionId, hub.ResourceGroup, hub.VnetName, hub.Location, hub.AddressPrefix.ToString()));
    }

    [Fact]
    public void Read_SupportsSemicolonsAndDifferentColumnOrder()
    {
        const string csv = "addressPrefix;vnetName\n10.2.0.0/16;vnet-a\n";

        var vnet = Assert.Single(VnetCsvReader.Read(csv).Vnets);

        Assert.Equal("vnet-a", vnet.VnetName);
        Assert.Equal("10.2.0.0/16", vnet.AddressPrefix.ToString());
    }

    [Fact]
    public void Read_ExpandsJsonArraysAndSkipsIpv6()
    {
        const string csv = "vnetName,addressPrefix\nvnet-a,\"[\"\"10.3.0.0/16\"\",\"\"10.4.0.0/16\"\",\"\"fd00:db8::/48\"\"]\"\n";

        var result = VnetCsvReader.Read(csv);

        Assert.Equal(["10.3.0.0/16", "10.4.0.0/16"], result.Vnets.Select(v => v.AddressPrefix.ToString()));
        Assert.Equal(1, result.Ipv6PrefixesSkipped);
    }

    [Fact]
    public void Read_WarnsAboutInvalidRows()
    {
        const string csv = "vnetName,addressPrefix\nvnet-a,\nvnet-b,not-an-ip\nvnet-c,10.9.0.0/16\n";

        var result = VnetCsvReader.Read(csv);

        Assert.Single(result.Vnets);
        Assert.Equal(2, result.Warnings.Count);
    }

    [Fact]
    public void Read_ThrowsWithoutPrefixColumn() =>
        Assert.Throws<InvalidDataException>(() => VnetCsvReader.Read("name,location\nvnet,westeurope\n"));
}
