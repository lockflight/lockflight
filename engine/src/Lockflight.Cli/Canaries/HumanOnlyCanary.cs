using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The self-test's human-only canary: <c>approve spec</c>, run with <c>CLAUDECODE=1</c> as the agent's shell has it, must refuse
/// before doing anything. It names a feature that does not exist, so even an approval that failed to refuse would stop at once.
/// The engine's canary suite runs every command reserved for a person.
/// </summary>
internal static class HumanOnlyCanary
{
    public const string Name = "human-only: 'approve spec' refuses under CLAUDECODE=1";

    public const string RefusalText = "does not run inside a Claude Code session";

    public static async Task<CanaryReport> RunAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        CanaryRun run = await scratch.RunAsAgentAsync(["approve", "spec", "--feature", "lockflight-canary-none"], string.Empty, cancellationToken);
        return run.ExitCode == ExitCode.HookBlock && run.Error.Contains(RefusalText, StringComparison.Ordinal)
            ? new CanaryReport(Name, CanaryResult.Caught, "refused")
            : new CanaryReport(Name, CanaryResult.Missed, $"it ran (exit {(int)run.ExitCode}): {run.FirstLine}");
    }
}
