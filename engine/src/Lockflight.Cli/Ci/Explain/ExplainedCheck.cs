namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// One check (or gate step) that did not pass: its contract id, display title, requirement (a check's level, a step's mode),
/// verdict, the detail line about this run, its reason, and for the gates check the gate steps that did not pass.
/// </summary>
internal sealed record ExplainedCheck(
    string Id,
    string Title,
    string Requirement,
    string Verdict,
    string Detail,
    ExplainedReason Reason,
    IReadOnlyList<ExplainedCheck> Steps);
