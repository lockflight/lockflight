namespace Lockflight.Cli.Ci;

/// <summary>What the judge concluded for one check.</summary>
internal enum CheckVerdict
{
    /// <summary>The check held over a non-empty scope.</summary>
    Pass = 0,

    /// <summary>The check could not vouch for the change (nothing to judge, or a person must act).</summary>
    NotEarned = 1,

    /// <summary>The check found a problem.</summary>
    Fail = 2,

    /// <summary>The engine or a tool broke; says nothing about the code.</summary>
    Error = 3,

    /// <summary>The check does not apply to this change or event; shown as "—".</summary>
    NotApplicable = 4,

    /// <summary>The check did not run (no API key, a fork, a skipped job); NOT-EARNED where it is required.</summary>
    Skipped = 5,
}
