using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>
/// A handler's answer: the decision the hook reports (a block is printed and recorded in the ledger), and standard output for the
/// events whose contract has one (the SessionStart banner, an expansion's JSON). A handler that wrote its answer itself (an
/// in-session command's digest) names the exit code instead.
/// </summary>
internal sealed record HookReply(HookDecision Decision, string? Output)
{
    /// <summary>The exit code of an answer the handler already wrote; null when the decision is to be reported.</summary>
    public ExitCode? WrittenExitCode { get; init; }

    public static HookReply Of(HookDecision decision) => new(decision, null);

    public static HookReply Written(ExitCode exitCode) => new(HookDecision.Allow, null) { WrittenExitCode = exitCode };
}
