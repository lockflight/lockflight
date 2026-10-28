using System.Text.Json;
using System.Text.RegularExpressions;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Finds the feature a lifecycle command works on: the <c>--feature</c> option (an ID or a path), else Spec Kit's active feature
/// (its variables, <c>.specify/feature.json</c>, the feature branch), else the only directory under <c>specs/</c>.
/// </summary>
internal static partial class FeatureLocator
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

    /// <summary>
    /// Whether <paramref name="id"/> is a feature ID that can only name a directory directly under <c>specs/</c>: not empty, not
    /// <c>.</c> or <c>..</c>, no path separator and no control character.
    /// </summary>
    public static bool IsPlainId(string id) =>
        id.Length > 0 && id is not "." and not ".." && id.IndexOfAny(['/', '\\']) < 0 && TerminalText.IsPlainName(id);

    public static FeatureLookup Resolve(string root, string? requested) => Resolve(root, requested, EnvironmentReader.Process);

    /// <summary>
    /// The feature, in the order Spec Kit itself uses after the explicit <paramref name="requested"/> one:
    /// <c>SPECIFY_FEATURE_DIRECTORY</c>, <c>.specify/feature.json</c>, <c>SPECIFY_FEATURE</c>, the feature branch, then the only
    /// feature. A name (the variable or the branch) is matched like Spec Kit's git extension: its last path segment is the
    /// directory under <c>specs/</c>, else its number (<c>006</c>, or a <c>YYYYMMDD-HHMMSS</c> timestamp) picks the one directory
    /// with that prefix. A setting that names nothing, and a number several directories share, is an error, never a guess.
    /// </summary>
    public static FeatureLookup Resolve(string root, string? requested, EnvironmentReader environment)
    {
        if (requested is not null)
        {
            return Existing(root, FromRequest(root, requested), $"feature '{requested}' not found under {SpecsDirectory}/");
        }

        if (environment.Get(EnvironmentVariables.SpecifyFeatureDirectory) is { Length: > 0 } configured)
        {
            return configured.IndexOfAny(Path.GetInvalidPathChars()) < 0
                ? Existing(root, Path.GetFullPath(configured, root), $"{EnvironmentVariables.SpecifyFeatureDirectory} '{configured}' is not a directory; set it to a feature directory, or unset it")
                : FeatureLookup.NotFound($"{EnvironmentVariables.SpecifyFeatureDirectory} is not a path; set it to a feature directory, or unset it");
        }

        switch (ReadSpecKitPointer(root))
        {
            case (null, string problem):
                return FeatureLookup.NotFound(problem);
            case (string pointed, null):
                return Existing(root, pointed, $"{SpecKitPointer} names {pointed}, which does not exist; pass --feature <id>, or run Spec Kit's feature command again to rewrite it");
        }

        if (environment.Get(EnvironmentVariables.SpecifyFeature) is { Length: > 0 } name)
        {
            return ByName(root, name, $"{EnvironmentVariables.SpecifyFeature} '{name}'")
                ?? FeatureLookup.NotFound($"{EnvironmentVariables.SpecifyFeature} '{name}' names no feature under {SpecsDirectory}/; pass --feature <id>, or unset it");
        }

        if (GitBranch.Current(root) is string branch && ByName(root, branch, $"branch '{branch}'") is FeatureLookup fromBranch)
        {
            return fromBranch;
        }

        return OnlyFeature(root) is string only
            ? Found(root, only)
            : FeatureLookup.NotFound($"no active feature: pass --feature <id> (no {SpecKitPointer}, Spec Kit variable or feature branch names one, and {SpecsDirectory}/ holds zero or several features)");
    }

    private static string FromRequest(string root, string requested) =>
        Directory.Exists(Path.Combine(root, requested)) && (requested.Contains('/', StringComparison.Ordinal) || requested.Contains('\\', StringComparison.Ordinal))
            ? Path.GetFullPath(requested, root)
            : Path.Combine(root, SpecsDirectory, requested);

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

    /// <summary>
    /// The feature a Spec Kit feature name names: its last path segment as a directory under <c>specs/</c>, else the one directory
    /// sharing its number; an error when several share it; null when it names none.
    /// </summary>
    private static FeatureLookup? ByName(string root, string name, string source)
    {
        string segment = name[(name.LastIndexOf('/') + 1)..];
        string specs = Path.Combine(root, SpecsDirectory);
        if (!IsPlainId(segment) || !Directory.Exists(specs))
        {
            return null;
        }

        if (Directory.Exists(Path.Combine(specs, segment)))
        {
            return Found(root, Path.Combine(specs, segment));
        }

        if (FeatureNumber(segment) is not string number)
        {
            return null;
        }

        string[] matches = [.. Directory.GetDirectories(specs, $"{number}-*").Order(StringComparer.Ordinal)];
        return matches.Length switch
        {
            0 => null,
            1 => Found(root, matches[0]),
            _ => FeatureLookup.NotFound(
                $"{source} matches several features under {SpecsDirectory}/: {string.Join(", ", matches.Select(Path.GetFileName))}; pass --feature <id>"),
        };
    }

    /// <summary>The feature number a name starts with, as Spec Kit numbers features: a timestamp, else three or more digits.</summary>
    private static string? FeatureNumber(string segment)
    {
        if (TimestampNumber().Match(segment) is { Success: true } timestamp)
        {
            return timestamp.Groups["number"].Value;
        }

        if (MalformedTimestamp().IsMatch(segment))
        {
            return null;
        }

        return SequentialNumber().Match(segment) is { Success: true } sequential ? sequential.Groups["number"].Value : null;
    }

    private static FeatureLookup Existing(string root, string directory, string missing) =>
        Directory.Exists(directory) ? Found(root, directory) : FeatureLookup.NotFound(missing);

    private static FeatureLookup Found(string root, string directory)
    {
        string id = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        return FeatureLookup.Found(new Feature(id, directory, Path.Combine(RecordsRoot(root), id)));
    }

    private static string? OnlyFeature(string root)
    {
        string specs = Path.Combine(root, SpecsDirectory);
        string[] features = Directory.Exists(specs) ? Directory.GetDirectories(specs) : [];
        return features.Length == 1 ? features[0] : null;
    }

    [GeneratedRegex(@"^(?<number>[0-9]{8}-[0-9]{6})-")]
    private static partial Regex TimestampNumber();

    /// <summary>A date one digit short, or a timestamp with no name after it: Spec Kit treats neither as a feature number.</summary>
    [GeneratedRegex(@"^(?:[0-9]{7}-[0-9]{6}-|[0-9]{7,8}-[0-9]{6}$)")]
    private static partial Regex MalformedTimestamp();

    [GeneratedRegex(@"^(?<number>[0-9]{3,})-")]
    private static partial Regex SequentialNumber();
}
