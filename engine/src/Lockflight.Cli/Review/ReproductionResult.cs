namespace Lockflight.Cli.Review;

/// <summary>What happened to a finding's reproduction in one review run.</summary>
internal enum ReproductionResult
{
    /// <summary>The finding has no reproduction (a minor or nit finding, or the reviewer gave none).</summary>
    None = 1,

    /// <summary>The reproduction was not run: the tests did not build or list.</summary>
    NotRun = 2,

    /// <summary>The reproduction compiled and failed on the current code.</summary>
    Failed = 3,

    /// <summary>The reproduction compiled and passed on the current code.</summary>
    Passed = 4,

    /// <summary>The reproduction did not compile, alone or with the others.</summary>
    DidNotCompile = 5,

    /// <summary>The reproduction compiled but the run did not report it as passed or failed (skipped, or not found).</summary>
    Inconclusive = 6,

    /// <summary>
    /// The reproduction did not compile, and the reviewer's repair of it compiled and passed. The finding stays unverified: a test
    /// rewritten to get past the compiler may have lost what it checked, so only a first reproduction can reject a finding.
    /// </summary>
    RepairedPassed = 7,
}
