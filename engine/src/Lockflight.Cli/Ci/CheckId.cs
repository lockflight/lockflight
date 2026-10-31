namespace Lockflight.Cli.Ci;

/// <summary>The checks the CI judge decides, each with its own verdict and requirement level.</summary>
internal enum CheckId
{
    /// <summary>Every red commit's new tests fail in production code (RED_STUB or RED_ASSERT), with at least one accepted cycle.</summary>
    Red = 1,

    /// <summary>Every cycle test is unchanged since its red, or changed and failing again on the red commit's code.</summary>
    TestsSinceRed = 2,

    /// <summary>Existing tests the pull request changed still tell the base's behaviour from the new one.</summary>
    TestsExistingChanged = 3,

    /// <summary>No new code in the test assemblies that could tamper with the test host's results.</summary>
    TestsHostTampering = 4,

    /// <summary>The whole suite passes at head, with every cycle test present.</summary>
    Green = 5,

    /// <summary>Every requirement of the spec is named by a test that went red, then green.</summary>
    Trace = 6,

    /// <summary>The <c>pr</c> gate profile at head with the base's policy.</summary>
    Gates = 7,

    /// <summary>The review of record, run in its own job.</summary>
    Review = 8,

    /// <summary>The approval facts git can show: CODEOWNERS on the base covering the specs, and spec-first.</summary>
    Approvals = 9,

    /// <summary>The change class from the base branch's routing policy, and its rules (a move out of a critical path fails).</summary>
    Routing = 10,

    /// <summary>The pull request's <c>lockflight.yaml</c> is no looser than the base's (<see cref="PolicyLoosening"/>).</summary>
    Policy = 11,
}
