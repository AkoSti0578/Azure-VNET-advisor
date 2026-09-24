namespace AzureSubnetPlanner.Core;

public sealed record SubnetRequest(string Name, int PrefixLength);

public sealed record SubnetAllocation(string Name, IPv4Network Network, bool IsFree = false)
{
    public long AzureUsableAddresses =>
        Math.Max(0, (long)Network.Size - SubnetLayoutPlanner.AzureReservedAddressesPerSubnet);

    /// <summary>Azure reserves the first four addresses and the last address of every subnet.</summary>
    public string UsableRangeText => Network.Size <= SubnetLayoutPlanner.AzureReservedAddressesPerSubnet
        ? "-"
        : $"{IPv4Network.FormatAddress(Network.FirstAddress + 4)} - {IPv4Network.FormatAddress(Network.LastAddress - 1)}";
}

public static class SubnetLayoutPlanner
{
    public const int AzureReservedAddressesPerSubnet = 5;

    /// <summary>Azure does not allow subnets smaller than /29.</summary>
    public const int SmallestAzureSubnetPrefix = 29;

    /// <summary>Smallest subnet (largest prefix length) that gives at least <paramref name="usableHosts"/> usable addresses in Azure.</summary>
    public static int PrefixForUsableHosts(long usableHosts)
    {
        for (var prefix = SmallestAzureSubnetPrefix; prefix >= 1; prefix--)
        {
            if ((1L << (32 - prefix)) - AzureReservedAddressesPerSubnet >= usableHosts)
            {
                return prefix;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(usableHosts), "Te veel hosts voor één IPv4-subnet.");
    }

    /// <summary>The smallest VNET prefix (largest prefix length) that fits all requested subnets.</summary>
    public static int RequiredVnetPrefix(IEnumerable<SubnetRequest> requests)
    {
        ulong total = 0;
        foreach (var request in requests)
        {
            total += 1UL << (32 - request.PrefixLength);
        }

        if (total == 0)
        {
            throw new ArgumentException("Er zijn geen subnets opgegeven.", nameof(requests));
        }

        if (total > 1UL << 32)
        {
            throw new ArgumentException("De subnets zijn samen groter dan de volledige IPv4-adresruimte.", nameof(requests));
        }

        var prefix = 32;
        while ((1UL << (32 - prefix)) < total)
        {
            prefix--;
        }

        return prefix;
    }

    /// <summary>
    /// Places the subnets inside the VNET. Largest subnets go first, which keeps every subnet
    /// aligned without wasting space. The result is sorted by address and includes the
    /// remaining free blocks (IsFree = true).
    /// </summary>
    public static IReadOnlyList<SubnetAllocation> Layout(IPv4Network vnet, IEnumerable<SubnetRequest> requests)
    {
        var allocations = new List<SubnetAllocation>();
        ulong cursor = vnet.FirstAddress;
        ulong end = vnet.LastAddress;

        foreach (var request in requests.OrderBy(r => r.PrefixLength))
        {
            if (request.PrefixLength < vnet.PrefixLength)
            {
                throw new InvalidOperationException(
                    $"Subnet '{request.Name}' (/{request.PrefixLength}) is groter dan het VNET {vnet}.");
            }

            var size = 1UL << (32 - request.PrefixLength);
            if (cursor + size - 1 > end)
            {
                throw new InvalidOperationException(
                    $"De subnets passen niet in {vnet}; minimaal /{RequiredVnetPrefix(requests)} is nodig.");
            }

            allocations.Add(new SubnetAllocation(request.Name, new IPv4Network((uint)cursor, request.PrefixLength)));
            cursor += size;
        }

        if (cursor <= end)
        {
            allocations.AddRange(AddressSpacePlanner.ToCidrBlocks(cursor, end)
                .Select(block => new SubnetAllocation("(vrij)", block, IsFree: true)));
        }

        return allocations;
    }
}
