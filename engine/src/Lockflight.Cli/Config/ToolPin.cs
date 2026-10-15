using System.Reflection;
using System.Text.Json;

namespace Lockflight.Cli.Config;

/// <summary>The engine version pinned by the project's .NET local tool manifest, and the version actually running.</summary>
internal static class ToolPin
{
    private const string PackageId = "lockflight.cli";
    private static readonly string[] ManifestLocations = ["dotnet-tools.json", Path.Combine(".config", "dotnet-tools.json")];

    /// <summary>The package version of this engine, prerelease suffix included (e.g. <c>0.1.0-alpha.1</c>).</summary>
    public static string RunningVersion { get; } = WithoutBuildMetadata(
        typeof(ToolPin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ToolPin).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0");

    /// <summary>SemVer build metadata ("+commit") is not part of the pinned version and is dropped before comparing.</summary>
    public static string WithoutBuildMetadata(string version)
    {
        int metadataStart = version.IndexOf('+', StringComparison.Ordinal);
        return metadataStart >= 0 ? version[..metadataStart] : version;
    }

    /// <summary>Pinned and running versions match when equal ignoring case (NuGet versions are case-insensitive).</summary>
    public static bool Matches(string pinned, string running) =>
        string.Equals(WithoutBuildMetadata(pinned), WithoutBuildMetadata(running), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The pinned version from <c>dotnet-tools.json</c> (root or <c>.config/</c>); null when the project pins none. Throws
    /// <see cref="InvalidDataException"/> when a tool manifest cannot be read; the hooks use <see cref="ReadPin"/>, which never throws.
    /// </summary>
    public static string? ReadPinnedVersion(string projectRoot)
    {
        ToolPinReading reading = ReadPin(projectRoot);
        return reading.Status == ToolPinStatus.Unreadable ? throw new InvalidDataException(reading.Problem) : reading.Version;
    }

    /// <summary>The engine pin of the project; a malformed or unreadable tool manifest is reported, not thrown.</summary>
    public static ToolPinReading ReadPin(string projectRoot)
    {
        foreach (string location in ManifestLocations)
        {
            string path = Path.Combine(projectRoot, location);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                if (ReadPinFrom(File.ReadAllText(path)) is string version)
                {
                    return ToolPinReading.Pinned(version);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                return ToolPinReading.Unreadable($"{location} cannot be read ({exception.Message})");
            }
        }

        return ToolPinReading.None;
    }

    /// <summary>The pinned version in a tool manifest's text; throws when its shape is wrong (a non-object, a non-string version).</summary>
    private static string? ReadPinFrom(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("the root is not a JSON object");
        }

        if (!document.RootElement.TryGetProperty("tools", out JsonElement tools))
        {
            return null;
        }

        // EnumerateObject throws on a "tools" that is not an object: a pin that cannot be read is not "no pin".
        foreach (JsonProperty tool in tools.EnumerateObject())
        {
            if (string.Equals(tool.Name, PackageId, StringComparison.OrdinalIgnoreCase)
                && tool.Value.ValueKind == JsonValueKind.Object
                && tool.Value.TryGetProperty("version", out JsonElement version))
            {
                // A null or non-string version is a pin that cannot be read, not "no pin".
                return version.ValueKind == JsonValueKind.String ? version.GetString() : throw new InvalidOperationException("the lockflight.cli version is not a string");
            }
        }

        return null;
    }
}
