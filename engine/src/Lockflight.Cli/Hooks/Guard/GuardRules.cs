using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// The PreToolUse guard: the one place its rules are registered, in order, in two stages. Once per call, on the input alone:
/// lockdown (which, when active, replaces the engine pin check), the engine pin, the relay guard; so a stale engine or a broken
/// test lock can never block the recovery command through an exception while facts are built. Then per root, the project and
/// each agent worktree under it (<c>.claude/worktrees/*</c>, a full checkout whose copies get the same rules at their own root), and
/// any other checkout of a Lockflight project the call writes into (<see cref="OtherProjectRoots"/>, such as a worktree in /tmp):
/// human-only commands, protected paths, the user's install (<c>~/.lockflight</c>). The first block wins. The plugin API and unknown-event checks apply to every hook event,
/// so they stay in <see cref="HookCommand"/>, ahead of all of these.
/// </summary>
internal static class GuardRules
{
    /// <summary>The rules that judge the call once, on its input, before any path work.</summary>
    public static IReadOnlyList<IGuardRule> OncePerCall { get; } = [new LockdownRule(), new EnginePinRule(), new SessionRelayRule()];

    /// <summary>The rules that judge the call at each root, built for one invocation.</summary>
    public static IReadOnlyList<IGuardRule> PerRoot(CliContext context, DirectoryReads reads) =>
        [new HumanOnlyCommandRule(), new ProtectedPathRule(context.Error, context.Environment, reads), new UserInstallRule(context.Environment)];

    public static async Task<HookDecision> DecideAsync(HookRequest input, Manifest manifest, CliContext context, DirectoryReads reads, CancellationToken cancellationToken)
    {
        GuardFacts project = new(input, manifest);
        if (await FirstBlockAsync(OncePerCall, project, cancellationToken) is GuardBlock precondition)
        {
            return precondition.ToDecision();
        }

        IReadOnlyList<IGuardRule> perRoot = PerRoot(context, reads);
        if (await FirstBlockAsync(perRoot, project, cancellationToken) is GuardBlock atRoot)
        {
            return atRoot.ToDecision();
        }

        foreach (string worktree in AgentWorktrees.Under(manifest.Root).Concat(OtherProjectRoots.Of(project, context.Environment.HomeDirectory())))
        {
            if (await FirstBlockAsync(perRoot, new GuardFacts(input, manifest with { Root = worktree }), cancellationToken) is GuardBlock inWorktree)
            {
                return inWorktree.ToDecision();
            }
        }

        return HookDecision.Allow;
    }

    private static async Task<GuardBlock?> FirstBlockAsync(IReadOnlyList<IGuardRule> rules, GuardFacts facts, CancellationToken cancellationToken)
    {
        foreach (IGuardRule rule in rules)
        {
            if (await rule.CheckAsync(facts, cancellationToken) is GuardBlock block)
            {
                return block;
            }
        }

        return null;
    }
}
