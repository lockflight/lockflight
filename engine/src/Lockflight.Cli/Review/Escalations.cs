using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Raises a minor or nit finding to major when the reviewer under-rated a violated requirement: only when its spec quote is anchored
/// in the spec (<see cref="SpecQuotes"/>), the quote holds an RFC 2119 MUST or SHALL (upper case, as a whole word), and its
/// reproduction fails on the current code. <see cref="Propose"/> marks each candidate major before the reproductions run, so it goes
/// through the same pipeline as the reviewer's own majors (one repair within the run's budget, each build within the time limit);
/// <see cref="Settle"/> then keeps a reproduced one major, with its original severity, and returns every other candidate to the
/// reviewer's severity with the reason, before the fix checks. The decision is recorded in the run file either way.
/// </summary>
internal static partial class Escalations
{
    /// <summary>Whether the finding is a candidate: minor or nit, new in this run, an anchored MUST or SHALL quote and a runnable reproduction.</summary>
    public static bool IsCandidate(ReviewFinding finding) =>
        finding is { Severity: FindingSeverity.Minor or FindingSeverity.Nit, Carried: false, SpecAnchor: SpecAnchor.Anchored, Reproduction: string reproduction }
        && Keyword(finding.SpecQuote) is not null
        && Reproductions.MethodName(reproduction) is not null;

    /// <summary>A candidate as a major finding that remembers its original severity; any other finding as it is.</summary>
    public static ReviewFinding Propose(ReviewFinding finding) =>
        IsCandidate(finding) ? finding with { Severity = FindingSeverity.Major, OriginalSeverity = finding.Severity } : finding;

    /// <summary>
    /// A proposed finding after its reproduction ran: escalated when it was confirmed, else back at its original severity, reported,
    /// with the reason it was not raised. <paramref name="repairable"/> are the ids whose reproductions could be repaired, so one that
    /// did not compile and was not repaired was left so by the budget.
    /// </summary>
    public static ReviewFinding Settle(ReviewFinding finding, IReadOnlySet<string> repairable)
    {
        if (finding is not { OriginalSeverity: FindingSeverity original, Escalation: SeverityEscalation.None })
        {
            return finding;
        }

        if (finding.Outcome == FindingOutcome.Confirmed)
        {
            return finding with { Escalation = SeverityEscalation.Escalated };
        }

        SeverityEscalation reason = finding.Reproduced switch
        {
            ReproductionResult.Passed or ReproductionResult.RepairedPassed => SeverityEscalation.ReproductionPassed,
            ReproductionResult.DidNotCompile when finding.ReproductionAttempts < 2 && repairable.Contains(finding.Id) => SeverityEscalation.BudgetExhausted,
            ReproductionResult.DidNotCompile => SeverityEscalation.ReproductionDidNotCompile,
            _ => SeverityEscalation.ReproductionNotRun,
        };
        return finding with { Severity = original, OriginalSeverity = null, Outcome = FindingOutcome.Reported, Escalation = reason };
    }

    /// <summary>The first RFC 2119 MUST or SHALL in the quote (upper case, a whole word); null when it has none.</summary>
    public static string? Keyword(string? quote) =>
        quote is not null && RequirementKeyword().Match(quote) is { Success: true } match ? match.Value : null;

    [GeneratedRegex(@"\b(?:MUST|SHALL)\b")]
    private static partial Regex RequirementKeyword();
}
