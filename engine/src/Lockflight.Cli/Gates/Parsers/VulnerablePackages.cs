using System.Text.Json;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Reads <c>dotnet package list --vulnerable --include-transitive --format json</c>. The command exits 0 even when it finds
/// advisories, so the gate judges the JSON, never the exit code. A report that judged nothing (no project listed, or a problem the
/// listing reports as an error) is not a clean one: it is refused with <see cref="InvalidOperationException"/>.
/// </summary>
internal static class VulnerablePackages
{
    private const string ErrorLevel = "error";

    public static IReadOnlyList<VulnerablePackage> Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (Children(document.RootElement, "problems").FirstOrDefault(IsError) is { ValueKind: JsonValueKind.Object } problem)
        {
            string text = problem.TryGetProperty("text", out JsonElement message) && message.ValueKind == JsonValueKind.String ? message.GetString()! : "(no text)";
            throw new InvalidOperationException($"the listing reports an error, so its packages were not all judged: {text}");
        }

        if (Children(document.RootElement, "projects").Length == 0)
        {
            throw new InvalidOperationException("the listing names no project, so no package was judged");
        }

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

    private static bool IsError(JsonElement problem) =>
        problem.ValueKind == JsonValueKind.Object
        && problem.TryGetProperty("level", out JsonElement level)
        && string.Equals(level.GetString(), ErrorLevel, StringComparison.OrdinalIgnoreCase);

    private static JsonElement[] Children(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement array) && array.ValueKind == JsonValueKind.Array ? [.. array.EnumerateArray()] : [];
}
