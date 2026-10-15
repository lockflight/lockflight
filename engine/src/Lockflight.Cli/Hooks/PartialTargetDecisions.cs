using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Decisions on write targets that are only partly known: paths below a directory a <c>cd</c> moved to that cannot be followed,
/// paths built from unknown variables, and name patterns below a directory (<c>find DIR -name P -delete</c>).
/// </summary>
internal static class PartialTargetDecisions
{
    private const string AnyDepthInfix = "**/";

    /// <summary>
    /// A relative path after a cd that cannot be followed may lie in any directory. The rule named is the most specific one that
    /// could match (the test lock or <c>secrets/**</c> rather than the catch-all <c>.lockflight/**</c>).
    /// </summary>
    public static HookDecision? Floating(ShellWrites writes, IReadOnlyList<string> globs, IReadOnlyList<(string Glob, string Reason)> testLocks)
    {
        IEnumerable<(string Path, bool AsTree)> floating = writes.FloatingFiles.Select(path => (path, false)).Concat(writes.FloatingTrees.Select(path => (path, true)));
        foreach ((string path, bool asTree) in floating)
        {
            string shown = path.Length == 0 ? "." : path;
            foreach (bool literalStart in new[] { true, false })
            {
                if (testLocks.FirstOrDefault(testLock => GlobOverlap.CouldMatchBelowAnyDirectory(testLock.Glob, path, asTree, literalStart)) is { Glob: not null } locked)
                {
                    return HookDecision.Block(
                        $"tests-locked:{locked.Glob}",
                        $"lockflight: '{shown}' is written after a cd the guard cannot follow, so it may be a test locked during {locked.Reason}. Use a literal cd target or a path from the project directory.");
                }

                if (globs.FirstOrDefault(glob => GlobOverlap.CouldMatchBelowAnyDirectory(glob, path, asTree, literalStart)) is string protectedGlob)
                {
                    return HookDecision.Block(
                        $"protected-path:{protectedGlob}",
                        $"lockflight: '{shown}' is written after a cd the guard cannot follow ($, ~, -, popd, a glob or a substitution), so it may be a path protected by rule '{protectedGlob}'. Use a literal cd target or a path from the project directory.");
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A target built from a variable the guard cannot know (<c>$X/secrets/k</c>) is blocked only when its literal end could be a
    /// protected path; <c>"$TMPDIR/x.log"</c> or a bare <c>"$FILE"</c> are allowed (a guardrail: variables are not followed).
    /// </summary>
    public static HookDecision? UnknownVariables(
        IEnumerable<(string Path, bool AsTree)> targets, string workingDirectory, string? home, IReadOnlyList<string> globs, IReadOnlyList<(string Glob, string Reason)> testLocks)
    {
        foreach ((string path, bool asTree) in targets)
        {
            if (ShellVariables.Resolve(path, workingDirectory, home).UnknownSuffix is not string suffix)
            {
                continue;
            }

            string? rule = globs.FirstOrDefault(glob => GlobOverlap.CouldMatchBelowAnyDirectory(glob, suffix, asTree, literalStart: true))
                ?? testLocks.Select(testLock => testLock.Glob).FirstOrDefault(glob => GlobOverlap.CouldMatchBelowAnyDirectory(glob, suffix, asTree, literalStart: true));
            if (rule is not null)
            {
                return HookDecision.Block(
                    "hidden-write-targets",
                    $"lockflight: '{path}' depends on a variable the guard cannot resolve, and '{suffix}' could be a path protected by rule '{rule}'. Write the path out instead.");
            }
        }

        return null;
    }

    /// <summary>
    /// A file whose name holds the output of a command (<c>logs/*.log</c> for <c>logs/$(date).log</c>) is blocked when that pattern
    /// could name a protected or locked path. A <c>..</c> after the computed part could lead anywhere and is blocked.
    /// </summary>
    public static HookDecision? Computed(
        IReadOnlyList<string> computedFiles, RootRelativePaths relatives, string workingDirectory, IReadOnlyList<string> globs, IReadOnlyList<(string Glob, string Reason)> testLocks)
    {
        foreach (string path in computedFiles)
        {
            string[] segments = path.Replace('\\', '/').Split('/');
            int firstComputed = Array.FindIndex(segments, GlobOverlap.HasWildcard);
            string prefix = firstComputed <= 0 ? ShellArguments.CurrentDirectory : string.Join('/', segments[..firstComputed]);
            if (relatives.Relative(prefix.Length == 0 ? "/" : prefix, workingDirectory) is not string relativePrefix)
            {
                continue;
            }

            string[] rest = firstComputed < 0 ? [] : segments[firstComputed..];
            string pattern = (relativePrefix.Length == 0 ? string.Empty : relativePrefix + "/") + string.Join('/', rest);
            string? rule = rest.Contains("..") ? "**"
                : globs.FirstOrDefault(glob => GlobOverlap.CouldMatch(glob, pattern, asTree: false))
                ?? testLocks.Select(testLock => testLock.Glob).FirstOrDefault(glob => GlobOverlap.CouldMatch(glob, pattern, asTree: false));
            if (rule is not null)
            {
                return HookDecision.Block(
                    "computed-write-target",
                    $"lockflight: '{path}' is partly computed by a command (shown as *), and could be a path protected by rule '{rule}'. Write the path out instead.");
            }
        }

        return null;
    }

    /// <summary><c>DIR/**/NAME</c>: blocked when an existing file of that name below the directory is protected or locked.</summary>
    public static HookDecision? NamePatterns(
        IReadOnlyList<string> patternFiles, ProtectedTreeScan scan, string root, string workingDirectory, IReadOnlyList<string> globs, IReadOnlyList<(string Glob, string Reason)> testLocks)
    {
        foreach (string patternFile in patternFiles)
        {
            int infix = patternFile.StartsWith(AnyDepthInfix, StringComparison.Ordinal) ? 0 : patternFile.LastIndexOf("/" + AnyDepthInfix, StringComparison.Ordinal) + 1;
            string start = infix == 0 ? ShellArguments.CurrentDirectory : patternFile[..(infix - 1)];
            if (ProtectedTreeScan.RelativeTree(root, start, workingDirectory) is not string relativeStart)
            {
                continue;
            }

            string name = patternFile[(infix + AnyDepthInfix.Length)..];
            string? rule = scan.FirstMatchNamed(relativeStart, name, globs) ?? scan.FirstMatchNamed(relativeStart, name, [.. testLocks.Select(testLock => testLock.Glob)]);
            if (rule is not null)
            {
                return HookDecision.Block(
                    $"protected-path:{rule}",
                    $"lockflight: this command deletes files named '{name}' below '{start}', which include files protected by rule '{rule}'. Narrow the pattern or name the files.");
            }
        }

        return null;
    }
}
