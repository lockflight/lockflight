namespace Lockflight.Cli.Review;

/// <summary>What the engine established about a finding.</summary>
internal enum FindingStatus
{
    /// <summary>Reported by the reviewer; no reproduction was required or attempted (minor and nit findings).</summary>
    Reported = 1,

    /// <summary>The reviewer's reproduction compiled and failed on the current code: the defect is real.</summary>
    Confirmed = 2,

    /// <summary>The reproduction compiled and passed: the finding is not reproduced.</summary>
    NotReproduced = 3,

    /// <summary>A blocker/major finding without a usable reproduction (missing, or it did not compile); a person must judge it.</summary>
    Unverified = 4,
}
