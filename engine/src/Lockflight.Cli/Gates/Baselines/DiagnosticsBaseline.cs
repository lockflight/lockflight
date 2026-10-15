using System.Text.Json.Nodes;
using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Accepted build warnings of a brownfield project (<c>.lockflight/baselines/build-diagnostics.json</c>, protected): fingerprint →
/// count. The build gate fails only on diagnostics beyond the baseline. Written only by <c>lockflight baseline build</c>.
/// </summary>
internal sealed class DiagnosticsBaseline(string projectRoot, string? unitPath)
{
    public const string FileName = "build-diagnostics.json";

    private const string Schema = "lockflight.baseline.build/v1";

    /// <summary>The file relative to the project root: under the unit's folder for a unit of a workspace.</summary>
    public string RelativePath => BaselinePaths.Relative(unitPath, FileName);

    private string FilePath => BaselinePaths.Full(projectRoot, unitPath, FileName);

    public bool Exists => File.Exists(FilePath);

    /// <summary>Diagnostics not covered by the baseline (a fingerprint seen more often than recorded counts as new).</summary>
    public IReadOnlyList<BuildDiagnostic> NewIn(IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        Dictionary<string, int> allowed = Load();
        List<BuildDiagnostic> fresh = [];
        foreach (BuildDiagnostic diagnostic in diagnostics)
        {
            if (allowed.TryGetValue(diagnostic.Fingerprint, out int remaining) && remaining > 0)
            {
                allowed[diagnostic.Fingerprint] = remaining - 1;
            }
            else
            {
                fresh.Add(diagnostic);
            }
        }

        return fresh;
    }

    public void Save(IReadOnlyList<BuildDiagnostic> diagnostics)
    {
        JsonObject counts = [];
        foreach (IGrouping<string, BuildDiagnostic> group in diagnostics.GroupBy(diagnostic => diagnostic.Fingerprint).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            counts[group.Key] = group.Count();
        }

        JsonObject document = new() { ["schema"] = Schema, ["diagnostics"] = counts };
        JsonFiles.Write(FilePath, document);
    }

    private Dictionary<string, int> Load()
    {
        if (!Exists || JsonNode.Parse(File.ReadAllText(FilePath))?["diagnostics"] is not JsonObject counts)
        {
            return [];
        }

        return counts.ToDictionary(entry => entry.Key, entry => entry.Value?.GetValue<int>() ?? 0, StringComparer.Ordinal);
    }
}
