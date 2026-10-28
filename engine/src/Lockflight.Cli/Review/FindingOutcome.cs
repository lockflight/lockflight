namespace Lockflight.Cli.Review;

/// <summary>What the engine established about a finding in one review run.</summary>
internal enum FindingOutcome
{
    /// <summary>Reported by the reviewer; no reproduction was required or attempted (minor and nit findings).</summary>
    Reported = 1,

    /// <summary>The reviewer's reproduction compiled and failed on the current code: the defect is real.</summary>
    Confirmed = 2,

    /// <summary>The reproduction compiled and passed on the current code: the finding is not reproduced.</summary>
    Rejected = 3,

    /// <summary>A blocker/major finding without a usable reproduction (missing, or it did not compile or run); a person must judge it.</summary>
    Unverified = 4,

    /// <summary>
    /// The reproduction failed, but the reviewer's own fix contradicts the change: it makes the reproduction pass and breaks one of
    /// the change's own tests, or it does not apply or build after one repair. A person must judge it.
    /// </summary>
    Contested = 5,
}
