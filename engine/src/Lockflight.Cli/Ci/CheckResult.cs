namespace Lockflight.Cli.Ci;

/// <summary>One check's outcome: its level for this change, its verdict, and why, in one line a person can act on.</summary>
internal sealed record CheckResult(CheckId Id, CheckLevel Level, CheckVerdict Verdict, string Why)
{
    public StatusContext Context => StatusContextNames.Of(Id);
}
