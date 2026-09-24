using System.Text;

namespace AzureSubnetPlanner.Core;

public sealed class VnetCsvImportResult
{
    public List<ExistingVnet> Vnets { get; } = [];

    public List<string> Warnings { get; } = [];

    public int DataRows { get; set; }

    public int Ipv6PrefixesSkipped { get; set; }
}

/// <summary>
/// Reads the CSV export of the Azure Resource Graph query (columns subscriptionName,
/// subscriptionId, resourceGroup, vnetName, location, addressPrefix). Comma, semicolon and
/// tab separated files are supported, as are unexpanded JSON arrays of prefixes.
/// </summary>
public static class VnetCsvReader
{
    private static readonly string[] PrefixColumns = ["addressprefix", "addressprefixes", "prefix", "addressspace", "cidr"];
    private static readonly string[] VnetColumns = ["vnetname", "name", "vnet", "virtualnetwork"];
    private static readonly string[] SubscriptionNameColumns = ["subscriptionname", "subscription"];
    private static readonly string[] SubscriptionIdColumns = ["subscriptionid"];
    private static readonly string[] ResourceGroupColumns = ["resourcegroup", "resourcegroupname"];
    private static readonly string[] LocationColumns = ["location", "region"];

    private static readonly char[] PrefixSeparators = ['[', ']', '"', '\'', ',', ';', ' ', '\t', '\r', '\n'];

    public static VnetCsvImportResult ReadFile(string path)
    {
        // FileShare.ReadWrite so the file can still be read while it is open in Excel.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return Read(reader.ReadToEnd());
    }

    public static VnetCsvImportResult Read(string content)
    {
        content = content.TrimStart('﻿');
        var result = new VnetCsvImportResult();
        var rows = ParseRows(content, DetectDelimiter(content));
        if (rows.Count == 0)
        {
            throw new InvalidDataException("Het CSV-bestand is leeg.");
        }

        var header = rows[0].Select(NormalizeHeader).ToList();
        var prefixIndex = FindColumn(header, PrefixColumns);
        if (prefixIndex < 0)
        {
            throw new InvalidDataException(
                "Kolom 'addressPrefix' niet gevonden in het CSV-bestand. Gebruik de export van de meegeleverde KQL-query.");
        }

        var vnetIndex = FindColumn(header, VnetColumns);
        var subscriptionNameIndex = FindColumn(header, SubscriptionNameColumns);
        var subscriptionIdIndex = FindColumn(header, SubscriptionIdColumns);
        var resourceGroupIndex = FindColumn(header, ResourceGroupColumns);
        var locationIndex = FindColumn(header, LocationColumns);

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            result.DataRows++;
            var lineNumber = r + 1;
            var vnetName = Field(row, vnetIndex);
            var rawPrefix = Field(row, prefixIndex);
            var tokens = rawPrefix.Split(PrefixSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                result.Warnings.Add($"Regel {lineNumber}: VNET '{vnetName}' heeft geen address prefix en is overgeslagen.");
                continue;
            }

            foreach (var token in tokens)
            {
                if (token.Contains(':'))
                {
                    result.Ipv6PrefixesSkipped++;
                    continue;
                }

                if (!IPv4Network.TryParse(token, out var prefix) || !token.Contains('/'))
                {
                    result.Warnings.Add($"Regel {lineNumber}: '{token}' van VNET '{vnetName}' is geen geldig address prefix en is overgeslagen.");
                    continue;
                }

                result.Vnets.Add(new ExistingVnet(
                    Field(row, subscriptionNameIndex),
                    Field(row, subscriptionIdIndex),
                    Field(row, resourceGroupIndex),
                    vnetName,
                    Field(row, locationIndex),
                    prefix));
            }
        }

        return result;
    }

    private static string Field(IReadOnlyList<string> row, int index) =>
        index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

    private static string NormalizeHeader(string header)
    {
        var builder = new StringBuilder(header.Length);
        foreach (var c in header)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    private static int FindColumn(List<string> header, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var index = header.IndexOf(candidate);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Picks the separator that occurs most often (outside quotes) in the header line.</summary>
    private static char DetectDelimiter(string content)
    {
        int commas = 0, semicolons = 0, tabs = 0;
        var inQuotes = false;
        foreach (var c in content)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c is '\n' or '\r')
                {
                    break;
                }

                if (c == ',') commas++;
                else if (c == ';') semicolons++;
                else if (c == '\t') tabs++;
            }
        }

        if (semicolons > commas && semicolons >= tabs)
        {
            return ';';
        }

        return tabs > commas ? '\t' : ',';
    }

    /// <summary>RFC 4180 parser: quoted fields, escaped quotes ("") and line breaks inside quotes.</summary>
    private static List<List<string>> ParseRows(string content, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
