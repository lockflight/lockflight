namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>How a new test behaved before implementation. Only stub and assertion reds are accepted.</summary>
internal enum RedClass
{
    /// <summary>Accepted: the test fails with <c>NotImplementedException</c> thrown by production code (a declared stub).</summary>
    RedStub = 1,

    /// <summary>Accepted: the test calls production code and fails on an assertion (xUnit or a recognised assertion or mocking library).</summary>
    RedAssert = 2,

    /// <summary>
    /// Rejected: another exception, or a <c>NotImplementedException</c> thrown outside the production code (by the runtime or a
    /// library), so the failure does not show the missing behaviour.
    /// </summary>
    RedOther = 3,

    /// <summary>Rejected: the test throws from its own code or its setup (a fixture, a constructor, <c>Assert.Fail</c>), so its failure says nothing about the production code.</summary>
    RedInTest = 4,

    /// <summary>Rejected: the test passes before implementation (in at least one data row).</summary>
    GreenBefore = 5,

    /// <summary>Rejected: the test is skipped or was not run.</summary>
    Skipped = 6,

    /// <summary>Rejected: repeated runs disagree.</summary>
    Flaky = 7,

    /// <summary>Rejected: the test fails on an assertion but never calls production code, so it cannot be about the change.</summary>
    RedNoProduction = 8,
}
