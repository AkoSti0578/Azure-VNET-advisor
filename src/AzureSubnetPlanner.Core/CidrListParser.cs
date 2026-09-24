namespace AzureSubnetPlanner.Core;

public sealed record CidrEntry(IPv4Network Network, string? Comment);

public sealed record CidrListParseResult(IReadOnlyList<CidrEntry> Entries, IReadOnlyList<string> Errors);

/// <summary>
/// Parses free text with CIDR ranges: one or more per line (separated by spaces, commas or
/// semicolons). Everything after '#' is a comment and is used as a description.
/// </summary>
public static class CidrListParser
{
    private static readonly char[] Separators = [' ', '\t', ',', ';'];

    public static CidrListParseResult Parse(string? text)
    {
        var entries = new List<CidrEntry>();
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CidrListParseResult(entries, errors);
        }

        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            string? comment = null;
            var hash = line.IndexOf('#');
            if (hash >= 0)
            {
                comment = line[(hash + 1)..].Trim();
                if (comment.Length == 0)
                {
                    comment = null;
                }

                line = line[..hash];
            }

            foreach (var token in line.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (IPv4Network.TryParse(token, out var network))
                {
                    entries.Add(new CidrEntry(network, comment));
                }
                else if (token.Contains(':'))
                {
                    errors.Add($"Line {i + 1}: IPv6 ('{token}') is not supported.");
                }
                else
                {
                    errors.Add($"Line {i + 1}: '{token}' is not a valid CIDR prefix (e.g. 192.168.0.0/16).");
                }
            }
        }

        return new CidrListParseResult(entries, errors);
    }
}
