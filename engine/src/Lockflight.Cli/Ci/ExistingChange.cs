namespace Lockflight.Cli.Ci;

/// <summary>How an existing (base) test the pull request changed did on the base's code.</summary>
internal enum ExistingChange
{
    /// <summary>The head's version fails on the base's code: it tells the old behaviour from the new.</summary>
    Rechecked = 1,

    /// <summary>The head's version passes on the base's code: it no longer tells them apart.</summary>
    PassesOnBase = 2,

    /// <summary>The head's test side does not build or run on the base's code.</summary>
    Uncheckable = 3,
}
