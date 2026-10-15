namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>How a new test behaved before implementation. Only stub and assertion reds are accepted.</summary>
internal enum RedClass
{
    /// <summary>Accepted: the test fails with <c>NotImplementedException</c> thrown by production code (a declared stub).</summary>
    RedStub = 1,

    /// <summary>Accepted: the test fails on an xUnit assertion.</summary>
    RedAssert = 2,

    /// <summary>Accepted with a flag: another exception thrown by production code (e.g. a stub returning null).</summary>
    RedOther = 3,

    /// <summary>Rejected: the test throws from its own code, so its failure says nothing about the production code.</summary>
    RedInTest = 4,

    /// <summary>Rejected: the test passes before implementation (in at least one data row).</summary>
    GreenBefore = 5,

    /// <summary>Rejected: the test is skipped or was not run.</summary>
    Skipped = 6,

    /// <summary>Rejected: repeated runs disagree.</summary>
    Flaky = 7,
}
