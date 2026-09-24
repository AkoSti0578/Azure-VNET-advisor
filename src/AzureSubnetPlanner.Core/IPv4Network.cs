using System.Globalization;

namespace AzureSubnetPlanner.Core;

/// <summary>
/// An IPv4 network in CIDR notation (for example 10.1.0.0/16). The address is always
/// normalized to the network address, so 10.1.2.3/16 becomes 10.1.0.0/16.
/// </summary>
public readonly record struct IPv4Network : IComparable<IPv4Network>
{
    public IPv4Network(uint address, int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(prefixLength), "Prefix length must be between 0 and 32.");
        }

        PrefixLength = prefixLength;
        Address = address & MaskFor(prefixLength);
    }

    public uint Address { get; }

    public int PrefixLength { get; }

    /// <summary>Total number of addresses in the network.</summary>
    public ulong Size => 1UL << (32 - PrefixLength);

    public uint FirstAddress => Address;

    public uint LastAddress => (uint)(Address + Size - 1);

    public string RangeText => $"{FormatAddress(FirstAddress)} - {FormatAddress(LastAddress)}";

    public static uint MaskFor(int prefixLength) => prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);

    public bool Contains(IPv4Network other) =>
        other.PrefixLength >= PrefixLength && (other.Address & MaskFor(PrefixLength)) == Address;

    public bool Overlaps(IPv4Network other) =>
        FirstAddress <= other.LastAddress && other.FirstAddress <= LastAddress;

    public int CompareTo(IPv4Network other)
    {
        var byAddress = Address.CompareTo(other.Address);
        return byAddress != 0 ? byAddress : PrefixLength.CompareTo(other.PrefixLength);
    }

    public override string ToString() => $"{FormatAddress(Address)}/{PrefixLength}";

    public static string FormatAddress(uint address) =>
        $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}";

    public static IPv4Network Parse(string text) =>
        TryParse(text, out var network)
            ? network
            : throw new FormatException($"'{text}' is not a valid IPv4 address prefix (expected e.g. 10.0.0.0/16).");

    /// <summary>
    /// Parses "a.b.c.d/nn". A bare address without prefix length is treated as /32.
    /// </summary>
    public static bool TryParse(string? text, out IPv4Network network)
    {
        network = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        var prefixLength = 32;
        var addressPart = text;
        var slash = text.IndexOf('/');
        if (slash >= 0)
        {
            addressPart = text[..slash];
            var prefixPart = text[(slash + 1)..];
            if (prefixPart.Length is 0 or > 2
                || !int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength)
                || prefixLength > 32)
            {
                return false;
            }
        }

        if (!TryParseAddress(addressPart, out var address))
        {
            return false;
        }

        network = new IPv4Network(address, prefixLength);
        return true;
    }

    /// <summary>Strict dotted-quad parser (IPAddress.TryParse also accepts forms like "10" or "10.1").</summary>
    public static bool TryParseAddress(string text, out uint address)
    {
        address = 0;
        var parts = text.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet)
                || octet > 255)
            {
                return false;
            }

            address = (address << 8) | (uint)octet;
        }

        return true;
    }
}
