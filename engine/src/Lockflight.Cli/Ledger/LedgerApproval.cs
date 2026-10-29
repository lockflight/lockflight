namespace Lockflight.Cli.Ledger;

/// <summary>
/// What an approval bound, without who gave it (the approval record holds that): the feature, the phase, each artifact with the
/// first characters of its hash (<c>tasks.md=1a2b3c4d5e6f,plan.md=…</c>) and where the person gave it.
/// </summary>
internal sealed record LedgerApproval(string Feature, string Phase, string Artifacts, ApprovalChannel Channel);
