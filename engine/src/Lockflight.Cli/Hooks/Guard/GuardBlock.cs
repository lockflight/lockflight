namespace Lockflight.Cli.Hooks.Guard;

/// <summary>
/// A guard rule's objection: the ledger code (<c>protected-path:secrets/**</c>) and the reason the agent and the person can act on.
/// There is no allow value, so composing rules can only add blocks.
/// </summary>
internal sealed record GuardBlock(string Code, string Reason)
{
    /// <summary>The block of a hook decision that blocks; null for one that allows.</summary>
    public static GuardBlock? Of(HookDecision decision) =>
        decision.Outcome == HookOutcome.Block ? new GuardBlock(decision.Code!, decision.Reason!) : null;

    public HookDecision ToDecision() => HookDecision.Block(Code, Reason);
}
