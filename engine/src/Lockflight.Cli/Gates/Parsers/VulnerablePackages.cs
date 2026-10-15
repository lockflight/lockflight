using System.Text.Json;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Reads <c>dotnet package list --vulnerable --include-transitive --format json</c>. The command exits 0 even when it finds
/// advisories, so the gate judges the JSON, never the exit code.
/// </summary>
internal static class VulnerablePackages
{
    public static IReadOnlyList<VulnerablePackage> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<VulnerablePackage> found = [];
        foreach (JsonElement framework in Children(document.RootElement, "projects").SelectMany(project => Children(project, "frameworks")))
        {
            foreach (JsonElement package in Children(framework, "topLevelPackages").Concat(Children(framework, "transitivePackages")))
            {
                string id = package.GetProperty("id").GetString() ?? "?";
                string version = package.TryGetProperty("resolvedVersion", out JsonElement resolved) ? resolved.GetString() ?? "?" : "?";
                foreach (JsonElement advisory in Children(package, "vulnerabilities"))
                {
                    found.Add(new VulnerablePackage(
                        id,
                        version,
                        advisory.TryGetProperty("severity", out JsonElement severity) ? severity.GetString() ?? "Unknown" : "Unknown",
                        advisory.TryGetProperty("advisoryurl", out JsonElement url) ? url.GetString() ?? string.Empty : string.Empty));
                }
            }
        }

        return [.. found.DistinctBy(package => package.Fingerprint)];
    }

    private static JsonElement[] Children(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];
}
