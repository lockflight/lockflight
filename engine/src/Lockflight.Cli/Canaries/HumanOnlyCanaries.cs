using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// Every command reserved for a person, run with <c>CLAUDECODE=1</c> as the agent's shell has it: each must refuse before doing
/// anything. The arguments name things that do not exist, so even a command that failed to refuse would stop at once, and it runs
/// in the scratch project with a home directory inside the scratch area.
/// </summary>
internal static class HumanOnlyCanaries
{
    private const string RefusalText = "does not run inside a Claude Code session";

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string[][] commands =
        [
            ["approve", "spec", "--feature", "lockflight-canary-none"],
            ["baseline", "tests", "--path", "lockflight-canary-none"],
            ["redgreen", "reset", "--feature", "lockflight-canary-none"],
            ["review", "dismiss", "lockflight-canary-none", "--reason", "canary"],
            ["adopt", "apply", "--skip-speckit"],
            ["adopt", "undo"],
            ["update", "--source", scratch.PathOf("lockflight-canary-no-feed")],
        ];
        List<CanaryReport> reports = [];
        foreach (string[] command in commands)
        {
            string name = $"human-only: '{string.Join(' ', command.Take(2))}' refuses under CLAUDECODE=1";
            CanaryRun run = await scratch.RunAsAgentAsync(command, string.Empty, cancellationToken);
            reports.Add(run.ExitCode == ExitCode.HookBlock && run.Error.Contains(RefusalText, StringComparison.Ordinal)
                ? new CanaryReport(name, CanaryResult.Caught, "refused")
                : new CanaryReport(name, CanaryResult.Missed, $"it ran (exit {(int)run.ExitCode}): {run.FirstLine}"));
        }

        return reports;
    }
}
