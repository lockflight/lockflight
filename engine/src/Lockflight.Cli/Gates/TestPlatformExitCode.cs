namespace Lockflight.Cli.Gates;

/// <summary>
/// Microsoft.Testing.Platform exit codes the test gate interprets
/// (https://learn.microsoft.com/dotnet/core/testing/microsoft-testing-platform-exit-codes). Any other value is an error.
/// </summary>
internal enum TestPlatformExitCode
{
    /// <summary>All selected tests passed.</summary>
    Success = 0,

    /// <summary>At least one test failed.</summary>
    TestsFailed = 2,

    /// <summary>The test application rejected its command line, e.g. an option of an extension the project does not reference.</summary>
    InvalidCommandLine = 5,

    /// <summary>No test ran — a vacuous result the gate reports as NOT-EARNED.</summary>
    ZeroTests = 8,
}
