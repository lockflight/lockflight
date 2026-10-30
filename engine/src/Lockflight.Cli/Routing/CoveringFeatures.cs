using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The features a pull request belongs to: every feature whose <c>specs/&lt;id&gt;/spec.md</c> or <c>tasks.md</c> the change adds or
/// edits, and the feature its branch names (the branch's last segment as a directory under <c>specs/</c> at head, else the one
/// feature that shares its number). Touching an old feature's other files does not make it covering.
/// </summary>
internal static partial class CoveringFeatures
{
    private const string SpecsDirectory = "specs/";
    private static readonly string[] CoveringFiles = ["spec.md", "tasks.md"];

    public static IReadOnlyList<string> Find(ChangeSet changeSet, string? branch)
    {
        SortedSet<string> features = new(StringComparer.Ordinal);
        foreach (ChangedFile file in changeSet.Files.Where(file => file.ExistsAtHead))
        {
            string[] segments = file.Path.Split('/');
            if (segments.Length == 3 && segments[0] + "/" == SpecsDirectory && CoveringFiles.Contains(segments[2], StringComparer.OrdinalIgnoreCase))
            {
                features.Add(segments[1]);
            }
        }

        if (branch is not null && ByBranch(changeSet.Head, branch) is string named)
        {
            features.Add(named);
        }

        return [.. features];
    }

    private static string? ByBranch(RevisionSnapshot head, string branch)
    {
        string segment = branch[(branch.LastIndexOf('/') + 1)..];
        HashSet<string> features = new(
            head.Paths.Where(path => path.StartsWith(SpecsDirectory, StringComparison.Ordinal) && path.Count(character => character == '/') >= 2)
                .Select(path => path.Split('/')[1]),
            StringComparer.Ordinal);
        if (features.Contains(segment))
        {
            return segment;
        }

        if (FeatureNumber().Match(segment) is not { Success: true } number)
        {
            return null;
        }

        string[] matches = [.. features.Where(feature => feature.StartsWith(number.Groups["number"].Value + "-", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];
        return matches.Length == 1 ? matches[0] : null;
    }

    [GeneratedRegex(@"^(?<number>[0-9]{8}-[0-9]{6}|[0-9]{3,})-")]
    private static partial Regex FeatureNumber();
}
