namespace Lockflight.Cli.Ci;

/// <summary>The host-tampering check: its verdict and line, the findings new in the pull request, and those the base already had.</summary>
internal sealed record TamperingJudgement(CheckVerdict Verdict, string Why, IReadOnlyList<TamperingFinding> New, IReadOnlyList<TamperingFinding> Existing);
