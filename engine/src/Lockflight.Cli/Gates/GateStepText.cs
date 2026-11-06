using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Gates;

/// <summary>
/// How a gate run prints one step as it finishes: its verdict and time, the detail, and for a step that did not pass the Next line
/// of its reason with where to read more.
/// </summary>
internal static class GateStepText
{
    public static async Task WriteAsync(TextWriter writer, GateStep step)
    {
        await writer.WriteLineAsync($"{step.Name}: {VerdictNames.ToContractName(step.Verdict)} ({step.DurationMs} ms)");
        if (step.Detail.Length > 0)
        {
            await writer.WriteLineAsync($"  {step.Detail}");
        }

        if (!VerdictNames.AllowsNextStep(step.Verdict) && ReasonCatalogue.ConsoleLine(step.Reason) is string next)
        {
            await writer.WriteLineAsync($"  {next}");
        }
    }
}
