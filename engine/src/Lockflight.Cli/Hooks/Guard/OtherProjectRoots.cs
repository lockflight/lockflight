using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// The roots of other checkouts of a Lockflight project that a call writes into: a target (a <c>git worktree add /tmp/wt</c>, a
/// second clone, a checkout nested in the project) whose nearest folder above it holding a <c>lockflight.yaml</c> is not the
/// session's root. A wildcard is judged by the folder before its first wildcard, so one above other checkouts
/// (<c>rm -rf /tmp/*</c>) is not. The guard
/// applies the per-root rules there too, as it does for agent worktrees under the project, so the normal way to work on a second
/// branch is not an unguarded one. A target whose variable the guard cannot resolve is not judged, as everywhere.
/// </summary>
internal static class OtherProjectRoots
{
    public static IReadOnlyList<string> Of(GuardFacts facts, string? home)
    {
        string workingDirectory = facts.Input.WorkingDirectory ?? facts.Manifest.Root;
        ShellWrites writes = facts.Writes;
        IEnumerable<string> targets = facts.IsFileEditingTool
            ? writes.Files
            : Literal(ProtectedPathRule.Resolved([.. writes.Files, .. writes.Trees, .. writes.SourceTrees], workingDirectory, home));

        // Inside the session's root too: a checkout nested in the project (git worktree add ./wt2, a vendored clone) has a root of
        // its own, which the session's root-anchored rules do not reach.
        string sessionRoot = PathRules.Canonicalize(facts.Manifest.Root, baseDirectory: null);
        SortedSet<string> roots = new(StringComparer.Ordinal);
        foreach (string target in targets)
        {
            if (RootAbove(target, workingDirectory) is string root
                && !string.Equals(PathRules.Canonicalize(root, baseDirectory: null), sessionRoot, StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(root);
            }
        }

        return [.. roots];
    }

    /// <summary>The literal targets, and for a wildcard the folder before its first wildcard.</summary>
    private static IEnumerable<string> Literal((List<string> Literal, List<string> Patterns) resolved) =>
        resolved.Literal.Concat(resolved.Patterns.Select(pattern => WildcardPatterns.Split(pattern).Prefix));

    /// <summary>The nearest folder at or above the target that holds a <c>lockflight.yaml</c>; null when none does or the path is odd.</summary>
    private static string? RootAbove(string target, string workingDirectory)
    {
        try
        {
            string path = PathRules.Canonicalize(target, workingDirectory);
            for (DirectoryInfo? directory = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, Manifest.FileName)))
                {
                    return directory.FullName;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return null;
        }

        return null;
    }
}
