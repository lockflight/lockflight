using System.Globalization;
using System.Text.Json;

namespace Lockflight.Cli.Ledger;

/// <summary>Reads the monthly JSONL ledger files; malformed lines are skipped (the ledger is fail-open and agent-writable — never an audit log).</summary>
internal static class LedgerReader
{
    public static IReadOnlyList<LedgerRecord> Read(string projectRoot, DateTimeOffset since)
    {
        string directory = Path.Combine(projectRoot, ".lockflight", "ledger");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<LedgerRecord> records = [];
        foreach (string file in Directory.EnumerateFiles(directory, "events-*.jsonl").Order(StringComparer.Ordinal))
        {
            foreach (string line in File.ReadLines(file))
            {
                if (Parse(line) is LedgerRecord record && record.At >= since)
                {
                    records.Add(record);
                }
            }
        }

        return records;
    }

    public static LedgerRecord? Parse(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (!DateTimeOffset.TryParse(Text(root, "ts"), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset at))
            {
                return null;
            }

            return new LedgerRecord(at, Text(root, "type"), Text(root, "signature"), Text(root, "summary"), Text(root, "detail") is { Length: > 0 } detail ? detail : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
