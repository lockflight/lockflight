using System.Text.Json;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Gates.Formatting;

/// <summary>
/// Reads the JSON report <c>dotnet format --report &lt;dir&gt;</c> writes (<c>format-report.json</c>): an array of documents, each with
/// a <c>FilePath</c> and <c>FileChanges</c> (<c>LineNumber</c>, <c>CharNumber</c>, <c>DiagnosticId</c>, <c>FormatDescription</c>).
/// </summary>
internal static class FormatReport
{
    public const string FileName = "format-report.json";

    /// <summary>The diagnostics in the report, paths relative to the project root; null when the report is not in the expected shape.</summary>
    public static IReadOnlyList<FormatDiagnostic>? Parse(string json, string projectRoot)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<FormatDiagnostic> diagnostics = [];
            foreach (JsonElement formattedFile in document.RootElement.EnumerateArray())
            {
                if (!ReadFile(formattedFile, projectRoot, diagnostics))
                {
                    return null;
                }
            }

            return [.. diagnostics.Distinct()];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ReadFile(JsonElement formattedFile, string projectRoot, List<FormatDiagnostic> diagnostics)
    {
        if (formattedFile.ValueKind != JsonValueKind.Object
            || !formattedFile.TryGetProperty("FilePath", out JsonElement filePath) || filePath.GetString() is not string path
            || !formattedFile.TryGetProperty("FileChanges", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        string file = PathRules.ToRelative(projectRoot, path) ?? path;
        foreach (JsonElement change in changes.EnumerateArray())
        {
            if (change.ValueKind != JsonValueKind.Object
                || !change.TryGetProperty("LineNumber", out JsonElement line) || !line.TryGetInt32(out int lineNumber)
                || !change.TryGetProperty("CharNumber", out JsonElement column) || !column.TryGetInt32(out int columnNumber))
            {
                return false;
            }

            string id = change.TryGetProperty("DiagnosticId", out JsonElement diagnosticId) ? diagnosticId.GetString() ?? string.Empty : string.Empty;
            string description = change.TryGetProperty("FormatDescription", out JsonElement text) ? text.GetString() ?? string.Empty : string.Empty;
            diagnostics.Add(new FormatDiagnostic(file, lineNumber, columnNumber, id, description));
        }

        return true;
    }
}
