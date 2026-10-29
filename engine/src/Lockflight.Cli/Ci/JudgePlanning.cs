using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Ci;

/// <summary>The plan, or the exit code and message that stopped it (configuration problems exit 4, git failures exit 3).</summary>
internal sealed record JudgePlanning(JudgePlan? Plan, ExitCode Failure, string? Error)
{
    public static JudgePlanning Planned(JudgePlan plan) => new(plan, ExitCode.Pass, null);

    public static JudgePlanning Failed(ExitCode failure, string error) => new(null, failure, error);
}
