namespace AzureSubnetPlanner.Core;

public static class KqlQueries
{
    /// <summary>Azure Resource Graph query that exports all VNET address prefixes.</summary>
    public const string ExistingVnets = """
        resources
        | where type =~ 'microsoft.network/virtualnetworks'
        | mv-expand addressPrefix = properties.addressSpace.addressPrefixes
        | join kind=leftouter (
            resourcecontainers
            | where type =~ 'microsoft.resources/subscriptions'
            | project subscriptionId, subscriptionName = name
          ) on subscriptionId
        | project subscriptionName, subscriptionId, resourceGroup, vnetName = name, location, addressPrefix = tostring(addressPrefix)
        | order by subscriptionName asc, vnetName asc
        """;
}
