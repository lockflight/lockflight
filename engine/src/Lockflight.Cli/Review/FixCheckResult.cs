namespace Lockflight.Cli.Review;

/// <summary>What happened when the engine applied a confirmed finding's fix in isolation and ran its reproduction and the change's own tests.</summary>
internal enum FixCheckResult
{
    /// <summary>No fix check: the finding gave no fix, was not confirmed in this run, or was open from an earlier run.</summary>
    None = 1,

    /// <summary>With the fix, the reproduction passes and every one of the change's own tests that ran passes.</summary>
    Held = 2,

    /// <summary>With the fix, the reproduction passes and at least one of the change's own tests fails: the finding is contested.</summary>
    BreaksOwnTest = 3,

    /// <summary>The fix is not a usable diff of existing non-test C# files, or its lines are not in them, also after one repair: contested.</summary>
    DidNotApply = 4,

    /// <summary>The fixed code, or the reproduction against it, does not compile, also after one repair: contested.</summary>
    DidNotBuild = 5,

    /// <summary>With the fix, the reproduction still fails: the fix does not show the expectation can be met, so nothing is checked.</summary>
    DidNotFix = 6,

    /// <summary>The fixed build did not run or did not report the reproduction (a time limit, a runner failure): nothing is checked.</summary>
    NotRun = 7,
}
