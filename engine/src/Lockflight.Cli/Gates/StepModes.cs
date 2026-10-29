using Lockflight.Cli.Config;

namespace Lockflight.Cli.Gates;

/// <summary>
/// What a step's <see cref="GateMode"/> does to its judgement: <c>off</c> turns the step into an N/A row that says so, <c>report</c>
/// shows a FAIL's findings on a PASS, and <c>fail</c> leaves the step as it judged. An ERROR is never softened: a check that could not
/// run must not read as green.
/// </summary>
internal static class StepModes
{
    /// <summary>The row of a step the manifest turned off; <paramref name="setting"/> is its key (<c>gates.quality.suppressions</c>).</summary>
    public static GateStep Off(string stepName, string setting) =>
        new(stepName, 0, Verdict.NotApplicable, 0, $"off by manifest ({setting}: off)");

    public static GateStep Apply(GateStep step, GateMode mode, string setting) =>
        mode == GateMode.Report && step.Verdict == Verdict.Fail
            ? step with { Verdict = Verdict.Pass, Detail = $"report only ({setting}: report; set it to fail to block): {step.Detail}" }
            : step;
}
