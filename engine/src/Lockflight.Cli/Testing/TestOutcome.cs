namespace Lockflight.Cli.Testing;

/// <summary>The runner's verdict for one executed test case.</summary>
internal enum TestOutcome
{
    /// <summary>The test ran and passed.</summary>
    Pass = 1,

    /// <summary>The test ran and failed (assertion or exception).</summary>
    Fail = 2,

    /// <summary>The test was skipped (<c>Skip = "…"</c>).</summary>
    Skip = 3,

    /// <summary>The test was selected but not run (e.g. explicit tests).</summary>
    NotRun = 4,
}
