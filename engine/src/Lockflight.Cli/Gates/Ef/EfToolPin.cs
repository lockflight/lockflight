using System.Text.Json;

namespace Lockflight.Cli.Gates.Ef;

/// <summary>
/// Whether a .NET tool manifest (<c>dotnet-tools.json</c> or <c>.config/dotnet-tools.json</c>) in the unit's directory or any directory
/// above it, up to the project root, pins <c>dotnet-ef</c>: the repository's own choice of the EF Core tools, which the
/// <c>ef.pendingModelChanges</c> step then runs. A manifest that cannot be read pins nothing.
/// </summary>
internal static class EfToolPin
{
    public const string ToolName = "dotnet-ef";

    private static readonly string[] ManifestLocations = ["dotnet-tools.json", Path.Combine(".config", "dotnet-tools.json")];

    /// <summary>The manifest that pins the tool, relative to the project root ('/'-separated); null when none does.</summary>
    public static string? Find(string projectRoot, string unitDirectory)
    {
        string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (DirectoryInfo? directory = new(Path.GetFullPath(unitDirectory)); directory is not null; directory = directory.Parent)
        {
            foreach (string location in ManifestLocations)
            {
                string path = Path.Combine(directory.FullName, location);
                if (File.Exists(path) && Pins(path))
                {
                    return Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                }
            }

            if (directory.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(root, StringComparison.Ordinal))
            {
                break;
            }
        }

        return null;
    }

    private static bool Pins(string manifest)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("tools", out JsonElement tools)
                && tools.ValueKind == JsonValueKind.Object
                && tools.EnumerateObject().Any(tool => tool.Name.Equals(ToolName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
