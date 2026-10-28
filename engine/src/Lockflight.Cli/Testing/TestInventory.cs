using System.Text.Json;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Reads a test application's listing (no tests run): xUnit's <c>-list full/json</c>, or MTP's <c>--xunit-list full</c> text for an
/// app that only accepts MTP options. <see cref="TestRunners"/> decides which runner lists an app.
/// </summary>
internal static class TestInventory
{
    /// <summary>
    /// Reads MTP's text listing: <c>- Display name: "…"</c>, then indented <c>Test method:  Class.Method</c>, optional
    /// <c>Skip reason:  "…"</c>, <c>Explicit:     true</c> and a <c>Traits:</c> block of <c>"Name": ["v1", "v2"]</c> lines. Theory rows repeat a method.
    /// </summary>
    public static IReadOnlyList<TestCase> ParseText(string assembly, string output)
    {
        List<TestCase> tests = [];
        string? method = null;
        string? skip = null;
        bool isExplicit = false;
        List<string> requirements = [];
        bool inTraits = false;

        void Flush()
        {
            int dot = method?.LastIndexOf('.') ?? -1;
            if (method is not null && dot > 0)
            {
                tests.Add(new TestCase(assembly, method[..dot], method[(dot + 1)..], [.. requirements], skip, isExplicit));
            }

            (method, skip, isExplicit, inTraits) = (null, null, false, false);
            requirements.Clear();
        }

        foreach (string raw in output.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("- Display name:", StringComparison.Ordinal))
            {
                Flush();
            }
            else if (line.StartsWith("Test method:", StringComparison.Ordinal))
            {
                method = line["Test method:".Length..].Trim();
                inTraits = false;
            }
            else if (line.StartsWith("Skip reason:", StringComparison.Ordinal))
            {
                skip = line["Skip reason:".Length..].Trim().Trim('"');
                inTraits = false;
            }
            else if (line.StartsWith("Explicit:", StringComparison.Ordinal))
            {
                isExplicit = string.Equals(line["Explicit:".Length..].Trim(), "true", StringComparison.OrdinalIgnoreCase);
                inTraits = false;
            }
            else if (line == "Traits:")
            {
                inTraits = true;
            }
            else if (inTraits && line.StartsWith("\"Requirement\":", StringComparison.Ordinal))
            {
                requirements.AddRange(line["\"Requirement\":".Length..].Trim().Trim('[', ']')
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(value => value.Trim('"')));
            }
        }

        Flush();
        return [.. tests.DistinctBy(test => test.Key)];
    }

    public static IReadOnlyList<TestCase>? Parse(string assembly, string output)
    {
        string? json = output.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith('['));
        if (json is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray()
            .Select(test => new TestCase(
                assembly,
                test.GetProperty("Class").GetString() ?? string.Empty,
                test.GetProperty("Method").GetString() ?? string.Empty,
                Requirements(test),
                test.TryGetProperty("Skip", out JsonElement skip) ? skip.GetString() : null,
                test.TryGetProperty("Explicit", out JsonElement isExplicit) && isExplicit.ValueKind == JsonValueKind.True))
            .DistinctBy(test => test.Key)];
    }

    private static string[] Requirements(JsonElement test) =>
        test.TryGetProperty("Traits", out JsonElement traits) && traits.TryGetProperty("Requirement", out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? [.. values.EnumerateArray().Select(value => value.GetString() ?? string.Empty).Where(value => value.Length > 0)]
            : [];
}
