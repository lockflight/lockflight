namespace Lockflight.Cli.Hooks.Guard;

/// <summary>One independent policy over a PreToolUse tool call. It can block; it cannot allow what another rule blocks.</summary>
internal interface IGuardRule
{
    /// <summary>A stable id for the registry and the canaries; the block codes a rule gives are its own.</summary>
    string Id { get; }

    /// <returns>A block, or null for no objection.</returns>
    ValueTask<GuardBlock?> CheckAsync(GuardFacts facts, CancellationToken cancellationToken);
}
