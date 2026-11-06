namespace Lockflight.Cli.Ci;

/// <summary>
/// How an existing (base) test the pull request changed was re-checked: its head version on the base's code, or, in a
/// structural-only change, its base version on the head's code.
/// </summary>
internal enum ExistingChange
{
    /// <summary>The head's version fails on the base's code: it tells the old behaviour from the new.</summary>
    Rechecked = 1,

    /// <summary>The head's version passes on the base's code: it no longer tells them apart.</summary>
    PassesOnBase = 2,

    /// <summary>The head's test side does not build or run on the base's code.</summary>
    Uncheckable = 3,

    /// <summary>Structural-only change: the base's version passes on the head's code, so the behaviour it pinned is kept.</summary>
    Kept = 4,

    /// <summary>Structural-only change: the base's version fails on the head's code, so the change altered behaviour it pinned.</summary>
    OldVersionFails = 5,

    /// <summary>Structural-only change: the base's version does not build or run on the head's code (a renamed API, typically).</summary>
    OldVersionUncheckable = 6,
}
