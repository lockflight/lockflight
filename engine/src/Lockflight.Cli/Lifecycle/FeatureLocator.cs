using System.Text.Json;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Finds the feature a lifecycle command works on: the <c>--feature</c> option (an ID or a path), else Spec Kit's active feature
/// in <c>.specify/feature.json</c>, else the only directory under <c>specs/</c>.
/// </summary>
internal static class FeatureLocator
{
    public const string SpecsDirectory = "specs";

    private const string LockflightDirectory = ".lockflight";
    private const string FeaturesDirectory = "features";
    private const string SpecKitPointer = ".specify/feature.json";
    private const string FeatureDirectoryProperty = "feature_directory";

    /// <summary>Where Lockflight keeps per-feature evidence: <c>.lockflight/features/</c> under the project root.</summary>
    public static string RecordsRoot(string root) => Path.Combine(root, LockflightDirectory, FeaturesDirectory);

    /// <summary>
    /// The feature a file belongs to — one under <c>specs/&lt;id&gt;/</c> or <c>.lockflight/features/&lt;id&gt;/</c>; null for any
    /// other file. The path is relative to the project root and '/'-separated, as git prints it.
    /// </summary>
    public static string? FeatureIdOf(string relativePath) => relativePath.Split('/') switch
    {
        [SpecsDirectory, string id, _, ..] => id,
        [LockflightDirectory, FeaturesDirectory, string id, _, ..] => id,
        _ => null,
    };

    public static FeatureLookup Resolve(string root, string? requested)
    {
        if (requested is null && SpecKitPointerProblem(root) is string problem)
        {
            return FeatureLookup.NotFound(problem);
        }

        string? directory = requested is not null ? FromRequest(root, requested) : FromSpecKit(root) ?? OnlyFeature(root);
        if (directory is null || !Directory.Exists(directory))
        {
            return FeatureLookup.NotFound(requested is not null
                ? $"feature '{requested}' not found under {SpecsDirectory}/"
                : $"no active feature: pass --feature <id> (Spec Kit's .specify/feature.json is missing and {SpecsDirectory}/ holds zero or several features)");
        }

        string id = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return FeatureLookup.Found(new Feature(id, directory, Path.Combine(RecordsRoot(root), id)));
    }

    private static string FromRequest(string root, string requested) =>
        Directory.Exists(Path.Combine(root, requested)) && (requested.Contains('/', StringComparison.Ordinal) || requested.Contains('\\', StringComparison.Ordinal))
            ? Path.GetFullPath(requested, root)
            : Path.Combine(root, SpecsDirectory, requested);

    /// <summary>Spec Kit's active feature: <c>feature_directory</c> in <c>.specify/feature.json</c>; null when absent or unreadable.</summary>
    private static string? FromSpecKit(string root) => ReadSpecKitPointer(root) is (string directory, null) ? directory : null;

    /// <summary>Why <c>.specify/feature.json</c> exists but names no feature directory; null when it is absent or fine.</summary>
    private static string? SpecKitPointerProblem(string root) => ReadSpecKitPointer(root) is (null, string problem) ? problem : null;

    private static (string? Directory, string? Problem) ReadSpecKitPointer(string root)
    {
        string path = Path.Combine(root, SpecKitPointer);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        string invalid = $"{SpecKitPointer} is not a JSON object with a string \"{FeatureDirectoryProperty}\"; pass --feature <id>, or run Spec Kit's feature command again to rewrite it";
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(FeatureDirectoryProperty, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { Length: > 0 } directory
                && directory.IndexOfAny(Path.GetInvalidPathChars()) < 0
                ? (Path.GetFullPath(directory, root), null)
                : (null, invalid);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, invalid);
        }
    }

    private static string? OnlyFeature(string root)
    {
        string specs = Path.Combine(root, SpecsDirectory);
        string[] features = Directory.Exists(specs) ? Directory.GetDirectories(specs) : [];
        return features.Length == 1 ? features[0] : null;
    }
}
