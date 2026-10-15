namespace Lockflight.Cli.Gates;

/// <summary>
/// What a gate concluded about the code. Three-valued by design: a vacuous pass (nothing was checked) is NOT-EARNED and blocks
/// like FAIL.
/// </summary>
internal enum Verdict
{
    /// <summary>The gate checked a non-empty scope and everything held.</summary>
    Pass = 0,

    /// <summary>The gate ran but had nothing to judge (e.g. zero tests ran), so it cannot vouch for the code.</summary>
    NotEarned = 1,

    /// <summary>The gate found a problem in the code.</summary>
    Fail = 2,

    /// <summary>The gate itself could not run properly (tool crash, unexpected exit code); says nothing about the code.</summary>
    Error = 3,

    /// <summary>
    /// The step does not apply to this run (not configured, no changed lines of its kind, no base to compare with); it neither
    /// vouches for the code nor blocks it. A run takes the verdict of its last step that did apply.
    /// </summary>
    NotApplicable = 4,
}
