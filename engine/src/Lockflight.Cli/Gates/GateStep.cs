using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Gates;

/// <summary>
/// One tool run inside a gate (e.g. build, test) and how it was judged, with the stable reason code of a step that did not pass
/// (<see cref="ReasonCode.None"/> for one that passed or did not apply). <see cref="Scope"/> says what the step judged (e.g.
/// "3 changed C# files (format)") when that is the run's best answer to "what was checked"; null when the step does not say.
/// </summary>
internal sealed record GateStep(string Name, int ToolExitCode, Verdict Verdict, ReasonCode Reason, long DurationMs, string Detail, string? Scope = null)
{
    public ReasonCode Reason { get; init; } = ReasonCodeNames.Required(Verdict is not (Verdict.Pass or Verdict.NotApplicable), Reason);

    /// <summary>
    /// The step's stable id (<c>build</c>, <c>coverage.patch</c>) that consumers of the result key on; <see cref="Name"/> is display
    /// text and gains the unit in a workspace run.
    /// </summary>
    public string Id { get; init; } = Name;

    /// <summary>The unit a workspace run judged with this row; null for a row about the whole project.</summary>
    public string? Unit { get; init; }
}
