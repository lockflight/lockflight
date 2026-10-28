namespace Lockflight.Cli.Review;

/// <summary>
/// What the engine decided about raising a minor or nit finding to major (<see cref="Escalations"/>): only a finding whose spec quote
/// is an anchored MUST or SHALL sentence and whose reproduction fails on the current code is raised.
/// </summary>
internal enum SeverityEscalation
{
    /// <summary>The finding was not a candidate: it is blocker or major as the reviewer rated it, or its quote is not an anchored MUST or SHALL sentence, or it has no runnable reproduction.</summary>
    None = 1,

    /// <summary>Raised to major: the quote is an anchored MUST or SHALL sentence and the reproduction failed on the current code.</summary>
    Escalated = 2,

    /// <summary>Kept at the reviewer's severity: the reproduction passed on the current code.</summary>
    ReproductionPassed = 3,

    /// <summary>Kept at the reviewer's severity: the reproduction did not compile, also after its repair.</summary>
    ReproductionDidNotCompile = 4,

    /// <summary>Kept at the reviewer's severity: the reproduction did not compile and the run's budget was spent before it could be repaired.</summary>
    BudgetExhausted = 5,

    /// <summary>Kept at the reviewer's severity: the reproduction did not run or report (the tests did not build or list, or the time limit was hit).</summary>
    ReproductionNotRun = 6,
}
