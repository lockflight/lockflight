namespace Lockflight.Cli.Ledger;

/// <summary>
/// Ledger signatures written by one command and read by the retro; a change here changes how existing ledgers are read.
/// </summary>
internal static class LedgerSignatures
{
    public const string RedGreenStart = "redgreen:start";
    public const string RedGreenReset = "redgreen:reset";
    public const string RedPrefix = "redgreen:red:";
    public const string RedAccepted = RedPrefix + "accepted";
    public const string RedRejected = RedPrefix + "rejected";
    public const string RedCompileError = RedPrefix + "COMPILE_RED";
    public const string GreenAccepted = "redgreen:green:accepted";
    public const string GreenRejected = "redgreen:green:rejected";

    public const string ReviewPrefix = "review:";
    public const string ReviewChanges = ReviewPrefix + "changes";
    public const string ReviewUnverified = ReviewPrefix + "unverified";
    public const string ReviewClean = ReviewPrefix + "clean";

    public const string ApprovePrefix = "approve:";

    /// <summary>Marks a passing gate run inside a <c>gate:&lt;profile&gt;:&lt;verdict&gt;:&lt;steps&gt;</c> signature.</summary>
    public const string GatePassed = ":PASS";
}
