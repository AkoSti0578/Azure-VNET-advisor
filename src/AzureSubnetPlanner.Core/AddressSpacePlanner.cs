namespace AzureSubnetPlanner.Core;

/// <summary>Free-space statistics for one search pool.</summary>
public sealed record PoolFreeSpace(IPv4Network Pool, ulong FreeAddresses, int? LargestFreePrefix)
{
    public double FreePercentage => Pool.Size == 0 ? 0 : 100.0 * FreeAddresses / Pool.Size;
}

/// <summary>
/// Finds address blocks that do not overlap any reserved range (existing VNETs,
/// on-premises networks and ranges Azure does not allow).
/// </summary>
public sealed class AddressSpacePlanner
{
    private readonly List<ReservedRange> _reserved;
    private readonly List<(ulong Start, ulong End)> _merged;

    public AddressSpacePlanner(IEnumerable<ReservedRange> reserved)
    {
        _reserved = reserved.ToList();
        _merged = Merge(_reserved.Select(r => ((ulong)r.Network.FirstAddress, (ulong)r.Network.LastAddress)));
    }

    public IReadOnlyList<ReservedRange> Reserved => _reserved;

    public IReadOnlyList<ReservedRange> FindConflicts(IPv4Network candidate) =>
        _reserved.Where(r => r.Network.Overlaps(candidate)).OrderBy(r => r.Network).ToList();

    public bool IsFree(IPv4Network candidate) => !_reserved.Any(r => r.Network.Overlaps(candidate));

    /// <summary>
    /// Returns up to <paramref name="maxResults"/> free, correctly aligned blocks of the given
    /// prefix length, searching the pools in the order given (lowest address first within a pool).
    /// </summary>
    public IReadOnlyList<IPv4Network> FindFreeBlocks(IEnumerable<IPv4Network> pools, int prefixLength, int maxResults)
    {
        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength));
        }

        var results = new List<IPv4Network>();
        var seen = new HashSet<IPv4Network>();
        var blockSize = 1UL << (32 - prefixLength);

        foreach (var pool in pools)
        {
            if (prefixLength < pool.PrefixLength)
            {
                continue; // the requested block is larger than the pool itself
            }

            foreach (var (gapStart, gapEnd) in FreeGaps(pool))
            {
                var start = AlignUp(gapStart, blockSize);
                while (start + blockSize - 1 <= gapEnd)
                {
                    if (results.Count >= maxResults)
                    {
                        return results;
                    }

                    var block = new IPv4Network((uint)start, prefixLength);
                    if (seen.Add(block))
                    {
                        results.Add(block);
                    }

                    start += blockSize;
                }
            }
        }

        return results;
    }

    public PoolFreeSpace GetFreeSpace(IPv4Network pool)
    {
        ulong free = 0;
        ulong largest = 0;
        foreach (var (gapStart, gapEnd) in FreeGaps(pool))
        {
            free += gapEnd - gapStart + 1;
            largest = Math.Max(largest, LargestAlignedBlock(gapStart, gapEnd));
        }

        int? largestPrefix = largest == 0 ? null : 32 - Log2(largest);
        return new PoolFreeSpace(pool, free, largestPrefix);
    }

    /// <summary>Unreserved address intervals (inclusive) inside the pool.</summary>
    public IEnumerable<(ulong Start, ulong End)> FreeGaps(IPv4Network pool)
    {
        ulong cursor = pool.FirstAddress;
        ulong poolEnd = pool.LastAddress;

        foreach (var (start, end) in _merged)
        {
            if (end < cursor)
            {
                continue;
            }

            if (start > poolEnd)
            {
                break;
            }

            if (start > cursor)
            {
                yield return (cursor, Math.Min(start - 1, poolEnd));
            }

            cursor = end + 1;
            if (cursor > poolEnd)
            {
                yield break;
            }
        }

        yield return (cursor, poolEnd);
    }

    /// <summary>Splits an inclusive interval into the minimal list of aligned CIDR blocks.</summary>
    public static IEnumerable<IPv4Network> ToCidrBlocks(ulong start, ulong end)
    {
        var position = start;
        while (position <= end)
        {
            var size = MaxBlockAt(position, end);
            yield return new IPv4Network((uint)position, 32 - Log2(size));
            position += size;
        }
    }

    private static ulong LargestAlignedBlock(ulong start, ulong end)
    {
        ulong largest = 0;
        var position = start;
        while (position <= end)
        {
            var size = MaxBlockAt(position, end);
            largest = Math.Max(largest, size);
            position += size;
        }

        return largest;
    }

    private static ulong MaxBlockAt(ulong position, ulong end)
    {
        var alignment = position == 0 ? 1UL << 32 : position & (~position + 1);
        var remaining = end - position + 1;
        var size = alignment;
        while (size > remaining)
        {
            size >>= 1;
        }

        return size;
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) / alignment * alignment;

    private static int Log2(ulong powerOfTwo) => 63 - (int)ulong.LeadingZeroCount(powerOfTwo);

    private static List<(ulong Start, ulong End)> Merge(IEnumerable<(ulong Start, ulong End)> ranges)
    {
        var merged = new List<(ulong Start, ulong End)>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && range.Start <= merged[^1].End + 1)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.End, range.End));
            }
            else
            {
                merged.Add(range);
            }
        }

        return merged;
    }
}
