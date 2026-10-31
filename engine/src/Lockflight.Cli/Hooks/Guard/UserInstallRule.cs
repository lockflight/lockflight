using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// Blocks the agent's writes to the user's Lockflight install (<c>~/.lockflight</c>: <c>bin/lockflight</c> and the engines in
/// <c>tools/&lt;version&gt;</c>). The recovery command the guard allows, <c>~/.lockflight/bin/lockflight link --installed</c>, runs
/// that binary and links one of those engines, so an agent that could replace them would choose the judge. Edits, the parsed
/// shell writes and a tree or wildcard that holds the install are judged, as for protected paths; unparsed writes are not (a
/// guardrail; the <c>Edit(~/.lockflight/**)</c> deny rule and the sandbox are the platform's side).
/// </summary>
internal sealed class UserInstallRule(EnvironmentReader environment) : IGuardRule
{
    public const string RuleId = "user-install";

    public string Id => RuleId;

    public ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken) =>
        ValueTask.FromResult(environment.HomeDirectory() is string home && !RecoveryCommands.Allows(facts.Input) ? GuardBlock.Of(Decide(facts, home)) : null);

    private static HookDecision Decide(GuardFacts facts, string home)
    {
        string install = Path.Combine(home, ".lockflight");
        string workingDirectory = facts.Input.WorkingDirectory ?? facts.Manifest.Root;
        RootRelativePaths relatives = new(install);
        ShellWrites writes = facts.Writes;
        if (facts.IsFileEditingTool)
        {
            return writes.Files.FirstOrDefault(file => relatives.Relative(file, workingDirectory) is not null) is string edited ? Block(edited) : HookDecision.Allow;
        }

        (List<string> files, List<string> filePatterns) = ProtectedPathRule.Resolved(writes.Files, workingDirectory, home);
        (List<string> trees, List<string> treePatterns) = ProtectedPathRule.Resolved([.. writes.Trees, .. writes.SourceTrees], workingDirectory, home);
        if (files.Concat(trees).FirstOrDefault(target => relatives.Relative(target, workingDirectory) is not null) is string inside)
        {
            return Block(inside);
        }

        if (trees.FirstOrDefault(tree => relatives.ContainsRoot(tree, workingDirectory)) is string above)
        {
            return Block(above);
        }

        foreach ((string pattern, bool asTree) in filePatterns.Select(pattern => (pattern, false)).Concat(treePatterns.Select(pattern => (pattern, true))))
        {
            string[] segments = pattern.Replace('\\', '/').Split('/');
            int firstWildcard = Array.FindIndex(segments, GlobOverlap.HasWildcard);
            string prefix = firstWildcard == 0 ? ShellArguments.CurrentDirectory : string.Join('/', segments[..firstWildcard]);
            prefix = prefix.Length == 0 ? "/" : prefix;
            if (relatives.Relative(prefix, workingDirectory) is not null)
            {
                return Block(pattern);
            }

            if (relatives.ContainsRoot(prefix, workingDirectory)
                && PathRules.ToRelative(PathRules.Canonicalize(prefix, workingDirectory), PathRules.Canonicalize(install, baseDirectory: null)) is string below
                && GlobOverlap.CouldMatch(below, string.Join('/', segments[firstWildcard..]), asTree: true))
            {
                return Block(pattern);
            }
        }

        return HookDecision.Allow;
    }

    private static HookDecision Block(string target) => HookDecision.Block(
        $"{RuleId}:~/.lockflight",
        $"lockflight: '{target}' is in the user's Lockflight install (~/.lockflight), which only the user changes (update, engine install). The engine it holds is the one the recovery command links, so the agent never writes there.");
}
