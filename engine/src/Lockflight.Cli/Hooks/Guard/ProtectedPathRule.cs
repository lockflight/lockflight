using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// Blocks file-editing tools, and the common shell writes the guard parses (<see cref="ShellWriteTargets"/>), when they target a
/// protected path (built-in defaults plus the manifest's rules) or a test locked during a red cycle, except the test files a
/// person's amend made writable (named one by one; a command that rewrites a whole directory stays blocked). Trees and wildcards
/// are judged by the protected files that exist below them; a target with a variable the guard cannot resolve is not judged, and
/// a command whose writes the guard does not parse is allowed. A guardrail against accidents, not a boundary.
/// </summary>
internal sealed class ProtectedPathRule(TextWriter diagnostics, EnvironmentReader environment, DirectoryReads reads) : IGuardRule
{
    public const string RuleId = "protected-path";

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
        RootRelativePaths relatives = new(manifest.Root);

        if (facts.IsFileEditingTool)
        {
            // An editing tool's path is literal: no variables, no wildcards, no home.
            return await FileDecisionAsync(relatives, WindowsParentSegments.WithCollapsed(writes.Files, workingDirectory), manifest, globs, testLocks, amended, workingDirectory) ?? HookDecision.Allow;
        }

        (List<string> files, List<string> filePatterns) = Resolved(writes.Files, workingDirectory);
        (List<string> trees, List<string> treePatterns) = Resolved(writes.Trees, workingDirectory);
        (List<string> sourceTrees, List<string> sourceTreePatterns) = Resolved(writes.SourceTrees, workingDirectory);
        // A pattern written as a tree (rm -r) also names the files it matches: one walk judges both.
        treePatterns.AddRange(sourceTreePatterns);
        IReadOnlyList<string> collapsedTrees = WindowsParentSegments.WithCollapsed(trees, workingDirectory);
        IReadOnlyList<string> collapsedSourceTrees = WindowsParentSegments.WithCollapsed(sourceTrees, workingDirectory);

        ProtectedTreeScan scan = new(manifest.Root, [.. globs, .. testLocks.Select(testLock => testLock.Glob)], reads);
        return TreeDecision(scan, relatives, [.. collapsedTrees.Select(tree => (tree, false)), .. collapsedSourceTrees.Select(tree => (tree, true))], globs, testLocks, amended, workingDirectory)
            ?? await FileDecisionAsync(relatives, WindowsParentSegments.WithCollapsed([.. files, .. trees, .. sourceTrees, .. filePatterns, .. treePatterns], workingDirectory), manifest, globs, testLocks, amended, workingDirectory)
            ?? PatternDecision(scan, relatives, filePatterns.Except(treePatterns).Select(pattern => (pattern, false)).Concat(treePatterns.Select(pattern => (pattern, true))), globs, testLocks, amended, workingDirectory)
            ?? HookDecision.Allow;
    }

    /// <summary>
    /// The targets with <c>~</c>, <c>$HOME</c> and <c>$PWD</c> resolved, split into literal paths and wildcard patterns. A target
    /// with any other variable, or with the output of a command in it, is left out: the guard does not know what it names.
    /// </summary>
    private (List<string> Literal, List<string> Patterns) Resolved(IReadOnlyList<string> paths, string workingDirectory)
    {
        List<string> literal = [];
        List<string> patterns = [];
        string? home = environment.HomeDirectory();
        foreach (string path in paths)
        {
            if (ShellVariables.Resolve(ExpandHome(path, home), workingDirectory, home).Path is string resolved)
            {
                (PathWildcards.HasWildcard(resolved) ? patterns : literal).Add(resolved);
            }
        }

        return (literal, patterns);
    }

    private static string ExpandHome(string path, string? home) =>
        (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)) && home is not null
            ? home + path[1..]
            : path;

    /// <summary>
    /// A wildcard target (<c>rm tests/*.cs</c>, <c>rm -r bin/*</c>) is blocked when a protected or locked file that exists below
    /// the directory before its first wildcard matches it (or, for a tree, lies below a path it matches). The shell expands a
    /// wildcard only to what exists; one that matches nothing is passed on as written, which the file check already judged.
    /// </summary>
    private static HookDecision? PatternDecision(
        ProtectedTreeScan scan, RootRelativePaths relatives, IEnumerable<(string Pattern, bool AsTree)> patterns, IReadOnlyList<string> globs,
        IReadOnlyList<(string Glob, string Reason)> testLocks, IReadOnlySet<string> amended, string workingDirectory)
    {
        foreach ((string pattern, bool asTree) in patterns)
        {
            string[] segments = pattern.Replace('\\', '/').Split('/');
            int firstWildcard = Array.FindIndex(segments, GlobOverlap.HasWildcard);
            string prefix = firstWildcard == 0 ? ShellArguments.CurrentDirectory : string.Join('/', segments[..firstWildcard]);
            if (relatives.Relative(prefix.Length == 0 ? "/" : prefix, workingDirectory) is not string directory)
            {
                continue;
            }

            string relativePattern = (directory.Length == 0 ? string.Empty : directory + "/") + string.Join('/', segments[firstWildcard..]);
            List<string> lockGlobs = [.. testLocks.Select(testLock => testLock.Glob)];
            string? matched = scan.FirstMatchOfPattern(directory, relativePattern, [.. lockGlobs, .. globs], asTree, (file, glob) => lockGlobs.Contains(glob) && !globs.Contains(glob) && IsAmended(amended, file));
            if (matched is not null && testLocks.FirstOrDefault(testLock => testLock.Glob == matched) is { Glob: string lockedGlob, Reason: string reason })
            {
                return HookDecision.Block(
                    $"tests-locked:{lockedGlob}",
                    $"lockflight: '{pattern}' matches test files locked during {reason}: make the tests pass by changing production code, then run '{EngineCommand.Linked} redgreen green'. If a test itself is wrong, ask the user.");
            }

            if (matched is not null)
            {
                return HookDecision.Block(
                    $"protected-path:{matched}",
                    $"lockflight: '{pattern}' matches files protected by rule '{matched}'. Name the files to change instead; protected files are changed by a human or a lockflight command.");
            }
        }

        return null;
    }

    /// <summary>
    /// A command that may rewrite a whole directory (git restore ., rm -r, dotnet format) is blocked when a protected file or a
    /// locked test project could lie inside it. Conservative: a rule that might match under the directory counts.
    /// </summary>
    private static HookDecision? TreeDecision(
        ProtectedTreeScan scan, RootRelativePaths relatives, IReadOnlyList<(string Tree, bool SourceOnly)> trees, IReadOnlyList<string> globs,
        IReadOnlyList<(string Glob, string Reason)> testLocks, IReadOnlySet<string> amended, string workingDirectory)
    {
        List<(string Tree, bool SourceOnly, string RelativeTree)> resolved = [];
        foreach ((string tree, bool sourceOnly) in trees)
        {
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
