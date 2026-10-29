namespace Lockflight.Cli.Testing;

/// <summary>
/// The one place lifecycle test runners are registered, and the order an app's runner is probed in: an app the build marked as
/// VSTest (xUnit v2) lists from its assembly metadata only; any other app tries xUnit's console runner first, then MTP, unless an
/// earlier listing already found that only MTP works.
/// </summary>
internal static class TestRunners
{
    public static IReadOnlyList<ITestRunner> All(TimeProvider time) => [new VSTestRunner(time), new XunitConsoleRunner(time), new MtpRunner(time)];

    /// <summary>The runner for an app's mode; an app not probed yet runs on xUnit's console runner.</summary>
    public static ITestRunner For(RunnerMode mode, TimeProvider time) => mode switch
    {
        RunnerMode.VSTest => new VSTestRunner(time),
        RunnerMode.Mtp => new MtpRunner(time),
        _ => new XunitConsoleRunner(time),
    };

    /// <summary>Lists the app's tests with the first runner of its probe order that can; the listing names the mode that worked.</summary>
    public static async Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, TimeProvider time, CancellationToken cancellationToken)
    {
        RunnerMode[] probe = app.Mode switch
        {
            RunnerMode.VSTest => [RunnerMode.VSTest],
            RunnerMode.Mtp => [RunnerMode.Mtp],
            _ => [RunnerMode.Native, RunnerMode.Mtp],
        };
        foreach (RunnerMode mode in probe)
        {
            TestListing listing = await For(mode, time).ListAsync(app, workingDirectory, timeLimit, cancellationToken);
            if (listing.Listed)
            {
                return listing;
            }
        }

        return TestListing.NotListed;
    }
}
