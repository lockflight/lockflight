using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>
/// One check's outcome: its level for this change, its verdict, the stable reason code when it did not pass (<see cref="ReasonCode.None"/>
/// when it passed or did not apply), and why, in one line a person can act on. The verdict and the reason change only together,
/// through <see cref="Judged"/>, so a new verdict never keeps the reason of the old one.
/// </summary>
internal sealed record CheckResult(CheckId Id, CheckLevel Level, CheckVerdict Verdict, ReasonCode Reason, string Why)
{
    public CheckVerdict Verdict { get; private init; } = Verdict;

    public ReasonCode Reason { get; private init; } = ReasonCodeNames.Required(Verdict is not (CheckVerdict.Pass or CheckVerdict.NotApplicable), Reason);

    public StatusContext Context => StatusContextNames.Of(Id);

    /// <summary>The same check with a new verdict, the reason for it and why; a verdict that does not pass must name its reason.</summary>
    public CheckResult Judged(CheckVerdict verdict, ReasonCode reason, string why) => new(Id, Level, verdict, reason, why);
}
