using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// One check's outcome: its level for this change, its verdict, the stable reason code when it did not pass (<see cref="ReasonCode.None"/>
/// when it passed or did not apply), and why, in one line a person can act on.
/// </summary>
internal sealed record CheckResult(CheckId Id, CheckLevel Level, CheckVerdict Verdict, ReasonCode Reason, string Why)
{
    public ReasonCode Reason { get; init; } = ReasonCodeNames.Required(Verdict is not (CheckVerdict.Pass or CheckVerdict.NotApplicable), Reason);

    public StatusContext Context => StatusContextNames.Of(Id);
}
