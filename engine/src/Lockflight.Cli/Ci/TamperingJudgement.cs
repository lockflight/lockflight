using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>The host-tampering check: its verdict and line, the findings new in the pull request, and those the base already had.</summary>
internal sealed record TamperingJudgement(CheckVerdict Verdict, ReasonCode Reason, string Why, IReadOnlyList<TamperingFinding> New, IReadOnlyList<TamperingFinding> Existing);
