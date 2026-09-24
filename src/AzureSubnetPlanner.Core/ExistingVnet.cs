namespace AzureSubnetPlanner.Core;

/// <summary>One address prefix of an existing Azure VNET, as exported by the Resource Graph query.</summary>
public sealed record ExistingVnet(
    string SubscriptionName,
    string SubscriptionId,
    string ResourceGroup,
    string VnetName,
    string Location,
    IPv4Network AddressPrefix);
