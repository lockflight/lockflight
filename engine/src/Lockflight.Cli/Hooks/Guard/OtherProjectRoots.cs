using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// The roots of other checkouts of a Lockflight project that a call writes into: a target outside the session's root (a
/// <c>git worktree add /tmp/wt</c>, a second clone) whose nearest folder above it holds a <c>lockflight.yaml</c>. The guard
/// applies the per-root rules there too, as it does for agent worktrees under the project, so the normal way to work on a second
/// branch is not an unguarded one. A target whose variable the guard cannot resolve is not judged, as everywhere.
/// </summary>
internal static class OtherProjectRoots
{
    public static IReadOnlyList<string> Of(GuardFacts facts, string? home)
    {
        string workingDirectory = facts.Input.WorkingDirectory ?? facts.Manifest.Root;
        RootRelativePaths session = new(facts.Manifest.Root);
        ShellWrites writes = facts.Writes;
        IEnumerable<string> targets = facts.IsFileEditingTool
            ? writes.Files
            : Literal(ProtectedPathRule.Resolved([.. writes.Files, .. writes.Trees, .. writes.SourceTrees], workingDirectory, home));

        SortedSet<string> roots = new(StringComparer.Ordinal);
        foreach (string target in targets)
        {
            if (session.Relative(target, workingDirectory) is not null)
            {
                continue;
            }

            if (RootAbove(PathRules.Canonicalize(target, workingDirectory)) is string root && session.Relative(root, workingDirectory) is null)
            {
                roots.Add(root);
            }
        }

        return [.. roots];
    }

    /// <summary>The literal targets, and for a wildcard the folder before its first wildcard.</summary>
    private static IEnumerable<string> Literal((List<string> Literal, List<string> Patterns) resolved) =>
        resolved.Literal.Concat(resolved.Patterns.Select(pattern =>
        {
            string[] segments = pattern.Replace('\\', '/').Split('/');
            string prefix = string.Join('/', segments.TakeWhile(segment => !GlobOverlap.HasWildcard(segment)));
            return prefix.Length == 0 ? "/" : prefix;
        }));

    private static string? RootAbove(string path)
    {
        try
        {
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
