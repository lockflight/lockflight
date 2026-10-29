using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The one place lifecycle test runners are registered, and the order an app's runner is probed in: an app the build marked as
/// VSTest (xUnit v2) lists from its assembly metadata only; an NUnit or MSTest app on Microsoft.Testing.Platform tries the platform
/// first, then VSTest, and one without the platform VSTest only; any other app tries xUnit's console runner first, then MTP, unless
/// an earlier listing already found that only MTP works.
/// </summary>
internal static class TestRunners
{
    public static IReadOnlyList<ITestRunner> All(TimeProvider time) =>
    [
        new VSTestRunner(time), new XunitConsoleRunner(time), new MtpRunner(time),
        new NUnitRunner(time, TestRunnerKind.VSTest), new NUnitRunner(time, TestRunnerKind.MicrosoftTestingPlatform),
        new MSTestRunner(time, TestRunnerKind.VSTest), new MSTestRunner(time, TestRunnerKind.MicrosoftTestingPlatform),
    ];

    /// <summary>The runner for an app's mode; an app not probed yet runs on xUnit's console runner.</summary>
    public static ITestRunner For(RunnerMode mode, TimeProvider time) => mode switch
    {
        RunnerMode.VSTest => new VSTestRunner(time),
        RunnerMode.Mtp => new MtpRunner(time),
        RunnerMode.NUnit => new NUnitRunner(time, TestRunnerKind.VSTest),
        RunnerMode.NUnitMtp => new NUnitRunner(time, TestRunnerKind.MicrosoftTestingPlatform),
        RunnerMode.MSTest => new MSTestRunner(time, TestRunnerKind.VSTest),
        RunnerMode.MSTestMtp => new MSTestRunner(time, TestRunnerKind.MicrosoftTestingPlatform),
        _ => new XunitConsoleRunner(time),
    };

    /// <summary>Whether the app's tests are xUnit tests (v3, or v2 on VSTest): review reproductions are written for xUnit only.</summary>
    public static bool IsXunit(RunnerMode mode) => mode is RunnerMode.Unknown or RunnerMode.Native or RunnerMode.Mtp or RunnerMode.VSTest;

    /// <summary>
    /// Lists the app's tests with the first runner of its probe order that can; the listing names the mode that worked. When none
    /// can, the first runner's reason (what the test project needs) is kept.
    /// </summary>
    public static async Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, TimeProvider time, CancellationToken cancellationToken)
    {
        RunnerMode[] probe = app.Mode switch
        {
            RunnerMode.VSTest => [RunnerMode.VSTest],
            RunnerMode.Mtp => [RunnerMode.Mtp],
            RunnerMode.NUnit => [RunnerMode.NUnit],
            RunnerMode.NUnitMtp => [RunnerMode.NUnitMtp, RunnerMode.NUnit],
            RunnerMode.MSTest => [RunnerMode.MSTest],
            RunnerMode.MSTestMtp => [RunnerMode.MSTestMtp, RunnerMode.MSTest],
            _ => [RunnerMode.Native, RunnerMode.Mtp],
        };
        TestListing? unlisted = null;
        foreach (RunnerMode mode in probe)
        {
            TestListing listing = await For(mode, time).ListAsync(app, workingDirectory, timeLimit, cancellationToken);
            if (listing.Listed)
            {
                return listing;
            }

            unlisted ??= listing.Problem is null ? null : listing;
        }

        return unlisted ?? TestListing.NotListed;
    }
}
