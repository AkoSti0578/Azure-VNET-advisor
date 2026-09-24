using System.Globalization;
using AzureSubnetPlanner.Core;

namespace AzureSubnetPlanner.App.ViewModels;

/// <summary>An entry in the prefix-length drop-downs, e.g. "/24  (256 adressen)".</summary>
public sealed record PrefixOption(int PrefixLength, string Label)
{
    public static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public static IReadOnlyList<PrefixOption> VnetOptions { get; } =
        Enumerable.Range(8, SubnetLayoutPlanner.SmallestAzureSubnetPrefix - 7)
            .Select(p => new PrefixOption(p, $"/{p}  ({(1L << (32 - p)).ToString("N0", Dutch)} adressen)"))
            .ToList();

    public static IReadOnlyList<PrefixOption> SubnetOptions { get; } =
        Enumerable.Range(8, SubnetLayoutPlanner.SmallestAzureSubnetPrefix - 7)
            .Select(p => new PrefixOption(p, $"/{p}  ({UsableText(p)})"))
            .ToList();

    public static string UsableText(int prefixLength) =>
        $"{((1L << (32 - prefixLength)) - SubnetLayoutPlanner.AzureReservedAddressesPerSubnet).ToString("N0", Dutch)} bruikbaar";
}
