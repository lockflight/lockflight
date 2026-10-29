namespace Lockflight.Cli.Ledger;

/// <summary>
/// Ledger signatures written by one command and read by the status line and the crew's retro skill; a change here changes how
/// existing ledgers are read.
/// </summary>
internal static class LedgerSignatures
{
    public const string RedGreenStart = "redgreen:start";
    public const string RedGreenReset = "redgreen:reset";
    public const string RedGreenAmended = "redgreen:amended";
    public const string RedPrefix = "redgreen:red:";
    public const string RedAccepted = RedPrefix + "accepted";
    public const string RedRejected = RedPrefix + "rejected";
    public const string RedCompileError = RedPrefix + "COMPILE_RED";
    public const string RedAmendRechecked = RedPrefix + "amend-rechecked";
    public const string GreenAccepted = "redgreen:green:accepted";
    public const string GreenRejected = "redgreen:green:rejected";

    public const string ReviewPrefix = "review:";
    public const string ReviewChanges = ReviewPrefix + "changes";
    public const string ReviewUnverified = ReviewPrefix + "unverified";
    public const string ReviewClean = ReviewPrefix + "clean";
    public const string ReviewNotOfRecord = ReviewPrefix + "not-of-record";

    public const string FindingDismissed = "finding:dismissed";
    public const string FindingFixed = "finding:fixed";

    public const string ApprovePrefix = "approve:";

    public const string ProposalPrefix = "proposal:";

    /// <summary>Marks a passing gate run inside a <c>gate:&lt;profile&gt;:&lt;verdict&gt;:&lt;steps&gt;</c> signature.</summary>
    public const string GatePassed = ":PASS";
}
