using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// Every command reserved for a person (<see cref="CommandAudience.Human"/> in <see cref="CommandRegistry"/>), run with
/// <c>CLAUDECODE=1</c> as the agent's shell has it: each must refuse before doing anything. The arguments name things that do not
/// exist, so even a command that failed to refuse would stop at once, and it runs in the scratch project with a home directory
/// inside the scratch area. A reserved command without arguments here is MISSED.
/// </summary>
internal static class HumanOnlyCanaries
{
    private const string NoFeature = "lockflight-canary-none";

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> planted = new(StringComparer.Ordinal)
        {
            ["update"] = ["--source", scratch.PathOf("lockflight-canary-no-feed")],
            ["engine install"] = ["--source", scratch.PathOf("lockflight-canary-no-feed")],
            ["baseline build"] = ["--path", NoFeature],
            ["baseline tests"] = ["--path", NoFeature],
            ["baseline deps"] = ["--path", NoFeature],
            ["baseline suppressions"] = [],
            ["redgreen reset"] = ["--feature", NoFeature],
            ["redgreen amend"] = [NoFeature, "--reason", "canary", "--feature", NoFeature],
            ["review dismiss"] = [NoFeature, "--reason", "canary"],
            ["approve spec"] = ["--feature", NoFeature],
            ["approve tasks"] = ["--feature", NoFeature],
            ["approve"] = [NoFeature],
        };

        List<CanaryReport> reports = [];
        foreach (CommandDescriptor command in CommandRegistry.All.Where(command => command.Audience == CommandAudience.Human))
        {
            string words = command.Name;
            string name = $"human-only: '{words}' refuses under CLAUDECODE=1";
            if (!planted.TryGetValue(words, out string[]? arguments))
            {
                reports.Add(new CanaryReport(name, CanaryResult.Missed, CanaryCoverageTests.MissingHumanOnlyCanary) { Control = words });
                continue;
            }

            CanaryRun run = await scratch.RunAsAgentAsync([.. command.Words, .. arguments], string.Empty, cancellationToken);
            CanaryReport report = run.ExitCode == ExitCode.HookBlock && run.Error.Contains(HumanOnlyCanary.RefusalText, StringComparison.Ordinal)
                ? new CanaryReport(name, CanaryResult.Caught, "refused")
                : new CanaryReport(name, CanaryResult.Missed, $"it ran (exit {(int)run.ExitCode}): {run.FirstLine}");
            reports.Add(report with { Control = words });
        }

        return reports;
    }
}
