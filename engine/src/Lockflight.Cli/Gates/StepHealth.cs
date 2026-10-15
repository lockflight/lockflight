using Lockflight.Cli.Gates.Parsers;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates;

/// <summary>Turns a tool run that timed out or failed because of the machine into an ERROR step with the fix.</summary>
internal static class StepHealth
{
    private const int MillisecondsPerSecond = 1000;

    public static GateStep? Unhealthy(string stepName, ProcessResult toolRun, GateRunContext run)
    {
        if (toolRun.TimedOut)
        {
            return new GateStep(stepName, toolRun.ExitCode, Verdict.Error, toolRun.DurationMs,
                $"timed out after {toolRun.DurationMs / MillisecondsPerSecond} s ({run.BudgetSource})");
        }

        return toolRun.ExitCode != 0 && EnvironmentProblems.Recognize(toolRun.Combined) is EnvironmentProblem problem
            ? new GateStep(stepName, toolRun.ExitCode, Verdict.Error, toolRun.DurationMs, $"{problem.Code}: {problem.Explanation}")
            : null;
    }
}
