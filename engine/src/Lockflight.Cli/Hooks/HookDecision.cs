namespace Lockflight.Cli.Hooks;

/// <summary>
/// A hook's answer: the outcome and, when blocking, a stable <see cref="Code"/> (the ledger fingerprint, e.g.
/// <c>protected-path:secrets/**</c>) plus a reason the agent and the human can act on.
/// </summary>
internal sealed record HookDecision(HookOutcome Outcome, string? Code, string? Reason)
{
    public static HookDecision Allow { get; } = new(HookOutcome.Allow, null, null);

    /// <summary>The id of the guard rule that blocked (<see cref="Guard.IGuardRule.Id"/>); null for any other decision. Not written anywhere.</summary>
    public string? Rule { get; init; }

    public static HookDecision Block(string code, string reason) => new(HookOutcome.Block, code, reason);
}
