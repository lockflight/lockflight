using Lockflight.Cli.ApprovalsOfRecord;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The approvals check of one pull request: the facts git showed, the verdict and its line, and, when a person must act before the
/// check can pass, why (the blocked report's reason; null otherwise).
/// </summary>
internal sealed record ApprovalsJudgement(ApprovalsFacts Facts, CheckVerdict Verdict, string Why, BlockedReasonCode? Blocked);
