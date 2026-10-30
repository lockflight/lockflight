namespace Lockflight.Cli.Ci;

/// <summary>How the head's version of a test did on older code (a red commit's, or the base's) with the head's test side laid over it.</summary>
internal enum RecheckOutcome
{
    /// <summary>It failed in production code (RED_STUB or RED_ASSERT): it tells the old code from the new.</summary>
    Red = 1,

    /// <summary>It passed, or failed for another reason: it no longer tells them apart.</summary>
    NotRed = 2,

    /// <summary>The test side did not build there, the test there is not the head's, or it could not be run.</summary>
    Uncheckable = 3,
}
