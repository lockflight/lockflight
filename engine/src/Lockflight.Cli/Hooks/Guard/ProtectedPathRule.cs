using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// Blocks file-editing tools, and shell commands that visibly write, when they target a protected path (built-in defaults plus the
/// manifest's rules) or a test locked during a red cycle, except the test files a person's amend made writable (named one by one;
/// a command that rewrites a whole directory stays blocked). One algorithm over shared facts (expanded wildcards, test locks, tree
/// scans): hidden targets, editing tools, unknown variables, wildcard breadth, trees, files, computed targets, name patterns,
/// floating targets, in that order. A guardrail, not a boundary.
/// </summary>
internal sealed class ProtectedPathRule(TextWriter diagnostics, EnvironmentReader environment, DirectoryReads reads) : IGuardRule
{
    public const string RuleId = "protected-path";

    private const int MaxWildcardMatches = 2000;
    private const int MinSiblingsToPrime = 8;

    public string Id => RuleId;

    public async ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) => GuardBlock.Of(await DecideAsync(facts));

    private async Task<HookDecision> DecideAsync(GuardFacts facts)
    {
        // The facts are read in this order on purpose: the same order the guard always computed them in.
        HookInput input = facts.Input;
        Manifest manifest = facts.Manifest;
        IReadOnlyList<string> globs = facts.ProtectedGlobs;
        string workingDirectory = input.WorkingDirectory ?? manifest.Root;
        IReadOnlyList<(string Glob, string Reason)> testLocks = facts.TestLocks;
        IReadOnlySet<string> amended = facts.AmendedFiles;
        ShellWrites writes = facts.Writes;

        if (writes.Hidden is string hidden)
        {
            return HookDecision.Block(
                "hidden-write-targets",
                $"lockflight: '{hidden}' writes to paths the command line does not show, so they cannot be checked against protected paths. Name the files in the command instead.");
        }

        if (facts.IsFileEditingTool)
        {
            // An editing tool's path is literal: no variables, no wildcards, no home.
            return await FileDecisionAsync(new RootRelativePaths(manifest.Root), WindowsParentSegments.WithCollapsed(writes.Files, workingDirectory), manifest, globs, testLocks, amended, workingDirectory) ?? HookDecision.Allow;
        }

        IEnumerable<(string Path, bool AsTree)> allTargets = writes.Files.Select(path => (path, false))
            .Concat(writes.Trees.Concat(writes.SourceTrees).Select(path => (path, true)));
        if (PartialTargetDecisions.UnknownVariables(allTargets, workingDirectory, environment.HomeDirectory(), globs, testLocks) is HookDecision variable)
        {
            return variable;
        }

        IReadOnlyList<string>? expandedFiles = Expand(writes.Files, workingDirectory);
        IReadOnlyList<string>? expandedTrees = Expand(writes.Trees, workingDirectory);
        IReadOnlyList<string>? expandedSourceTrees = Expand(writes.SourceTrees, workingDirectory);
        if (expandedFiles is null || expandedTrees is null || expandedSourceTrees is null)
        {
            return HookDecision.Block("wildcard-too-broad", $"lockflight: a wildcard in this command matches more than {MaxWildcardMatches} paths; name the files or a narrower pattern.");
        }

        IReadOnlyList<string> files = WindowsParentSegments.WithCollapsed(expandedFiles, workingDirectory);
        IReadOnlyList<string> trees = WindowsParentSegments.WithCollapsed(expandedTrees, workingDirectory);
        IReadOnlyList<string> sourceTrees = WindowsParentSegments.WithCollapsed(expandedSourceTrees, workingDirectory);

        ProtectedTreeScan scan = new(manifest.Root, [.. globs, .. testLocks.Select(testLock => testLock.Glob)], reads);
        RootRelativePaths relatives = new(manifest.Root);
        return TreeDecision(scan, relatives, trees, sourceTrees, globs, testLocks, amended, workingDirectory)
            ?? await FileDecisionAsync(relatives, [.. files, .. trees, .. sourceTrees], manifest, globs, testLocks, amended, workingDirectory)
            ?? PartialTargetDecisions.Computed(writes.ComputedFiles, relatives, workingDirectory, globs, testLocks)
            ?? PartialTargetDecisions.NamePatterns(writes.PatternFiles, scan, manifest.Root, workingDirectory, globs, testLocks)
            ?? PartialTargetDecisions.Floating(writes, globs, testLocks)
            ?? HookDecision.Allow;
    }

    /// <summary>
    /// Wildcard targets are checked as the shell would expand them (existing entries) and as written (a pattern that matches
    /// nothing is passed on literally); <c>~</c>, <c>$HOME</c> and <c>$PWD</c> are resolved, and targets with other variables were
    /// judged by <see cref="PartialTargetDecisions.UnknownVariables"/>. Null when a pattern matches too many paths to check.
    /// </summary>
    private List<string>? Expand(IReadOnlyList<string> paths, string workingDirectory)
    {
        List<string> expanded = [];
        string? home = environment.HomeDirectory();
        IEnumerable<string> resolved = paths.Select(path => ShellVariables.Resolve(ExpandHome(path), workingDirectory, home).Path).OfType<string>();
        foreach (string path in resolved)
        {
            expanded.Add(path);
            if (!PathWildcards.HasWildcard(path))
            {
                continue;
            }

            if (PathWildcards.Expand(path, workingDirectory, MaxWildcardMatches, reads) is not IReadOnlyList<string> matches)
            {
                return null;
            }

            expanded.AddRange(matches);
        }

        return expanded;
    }

    private string ExpandHome(string path) =>
        (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)) && environment.HomeDirectory() is string home
            ? home + path[1..]
            : path;

    /// <summary>
    /// A command that may rewrite a whole directory (git restore ., rm -r, dotnet format) is blocked when a protected file or a
    /// locked test project could lie inside it. Conservative: a rule that might match under the directory counts.
    /// </summary>
    private static HookDecision? TreeDecision(
        ProtectedTreeScan scan, RootRelativePaths relatives, IReadOnlyList<string> trees, IReadOnlyList<string> sourceTrees, IReadOnlyList<string> globs,
        IReadOnlyList<(string Glob, string Reason)> testLocks, IReadOnlySet<string> amended, string workingDirectory)
    {
        List<(string Tree, bool SourceOnly, string RelativeTree)> resolved = [];
        foreach ((string tree, bool sourceOnly) in trees.Select(tree => (tree, false)).Concat(sourceTrees.Select(tree => (tree, true))))
        {
            if (PathWildcards.HasWildcard(tree))
            {
                continue;
            }

            if (relatives.Relative(tree, workingDirectory) is string relativeTree)
            {
                resolved.Add((tree, sourceOnly, relativeTree));
            }
            else if (relatives.ContainsRoot(tree, workingDirectory))
            {
                // An ancestor of the project holds every protected path; no scan is needed to know it.
                return HookDecision.Block(
                    "protected-path:project-root",
                    $"lockflight: this command may rewrite or delete '{tree}', the folder that contains this project and every protected path in it. Name the files to change instead.");
            }
        }

        // Many trees under one directory (a wildcard's expansion) are served by one walk of that directory.
        if (resolved.Count > MinSiblingsToPrime && CommonDirectory(resolved.Select(entry => entry.RelativeTree)) is { Length: > 0 } common)
        {
            scan.Prime(common, resolved.Count);
        }

        foreach ((string tree, bool sourceOnly, string relativeTree) in resolved)
        {
            if (!IsAmended(amended, relativeTree) && testLocks.FirstOrDefault(testLock => scan.CouldMatchUnder(relativeTree, testLock.Glob, sourceOnly: false)) is { Glob: not null } treeLock)
            {
                return HookDecision.Block(
                    $"tests-locked:{treeLock.Glob}",
                    $"lockflight: this command may rewrite '{tree}', which contains test files locked during {treeLock.Reason}: make the tests pass by changing production code, then run '{EngineCommand.Linked} redgreen green'. If a test itself is wrong, ask the user.");
            }

            if (globs.FirstOrDefault(glob => scan.CouldMatchUnder(relativeTree, glob, sourceOnly)) is string protectedGlob)
            {
                return HookDecision.Block(
                    $"protected-path:{protectedGlob}",
                    $"lockflight: this command may rewrite '{tree}', which can hold files protected by rule '{protectedGlob}'. Name the files to change instead; protected files are changed by a human or a lockflight command.");
            }
        }

        return null;
    }

    /// <summary>The deepest directory that holds every one of the '/'-separated paths ("" when only the root does).</summary>
    private static string CommonDirectory(IEnumerable<string> relativePaths)
    {
        string[]? common = null;
        foreach (string[] segments in relativePaths.Select(path => path.Split('/')))
        {
            string[] directory = segments[..^1];
            common = common is null ? directory : [.. common.Zip(directory).TakeWhile(pair => pair.First == pair.Second).Select(pair => pair.First)];
        }

        return common is null ? string.Empty : string.Join('/', common);
    }

    private async Task<HookDecision?> FileDecisionAsync(
        RootRelativePaths relatives,
        IReadOnlyList<string> targets,
        Manifest manifest,
        IReadOnlyList<string> globs,
        IReadOnlyList<(string Glob, string Reason)> testLocks,
        IReadOnlySet<string> amended,
        string workingDirectory)
    {
        foreach (string target in targets)
        {
            if (OperatingSystem.IsWindows() && WindowsPaths.HasShortNameSegment(WindowsPaths.ExpandShortNames(target)))
            {
                // An 8.3 alias we could not expand might point at a protected path.
                return HookDecision.Block("unresolved-short-name", $"lockflight: cannot resolve short (8.3) path '{target}'; use the full path");
            }

            if (OperatingSystem.IsWindows() && WindowsPaths.ToDosPath(target) is null)
            {
                // A volume, device or share path might name a protected file under another spelling.
                return HookDecision.Block("unresolved-device-path", $"lockflight: cannot tell which file '{target}' names (a volume, device or share path); use the drive path (C:\\…) instead");
            }

            string? relative = relatives.Relative(target, workingDirectory);
            string? matchedGlob = relative is { Length: > 0 } ? globs.FirstOrDefault(glob => PathRules.Matches(glob, relative)) : null;
            await WriteDiagnosticsAsync(target, workingDirectory, manifest, matchedGlob);
            if (matchedGlob is not null)
            {
                return HookDecision.Block(
                    $"protected-path:{matchedGlob}",
                    $"lockflight: '{target}' is protected by rule '{matchedGlob}'. Protected files are changed by a human or a lockflight command, not by the agent: write the complete new file outside the project and run 'lockflight propose <path> --from <file>', then give the user the git apply command it prints.");
            }

            if (relative is { Length: > 0 } && !IsAmended(amended, relative) && testLocks.FirstOrDefault(testLock => PathRules.Matches(testLock.Glob, relative)) is { Glob: string lockedGlob, Reason: string reason })
            {
                return HookDecision.Block(
                    $"tests-locked:{lockedGlob}",
                    $"lockflight: '{target}' is a test file and tests are locked during {reason}: make the tests pass by changing production code, then run '{EngineCommand.Linked} redgreen green'. If a test itself is wrong, stop and {HumanOnlyCommands.AskTheUser("redgreen amend <test> --reason \"<why>\"")}; never change it another way.");
            }
        }

        return null;
    }

    /// <summary>Whether the path names a test file an open amend made writable (exactly that file, not a directory above it).</summary>
    private static bool IsAmended(IReadOnlySet<string> amended, string relative) => amended.Any(file => PathRules.Matches(file, relative));

    private async Task WriteDiagnosticsAsync(string filePath, string workingDirectory, Manifest manifest, string? matchedGlob)
    {
        if (string.IsNullOrEmpty(environment.Get(EnvironmentVariables.Debug)))
        {
            return;
        }

        await diagnostics.WriteLineAsync(
            $"debug: match='{matchedGlob}' canonical='{PathRules.Canonicalize(filePath, workingDirectory)}' root='{PathRules.Canonicalize(manifest.Root, baseDirectory: null)}' file='{filePath}' cwd='{workingDirectory}'");
    }
}
