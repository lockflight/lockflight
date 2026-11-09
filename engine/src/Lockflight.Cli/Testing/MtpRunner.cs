using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Microsoft.Testing.Platform with xUnit's extension options, for apps that accept only those: <c>--xunit-list full</c>, then
/// <c>--filter-method … --report-xunit-xml</c> in batches.
/// </summary>
internal sealed class MtpRunner(TimeProvider time) : ITestRunner
{
    /// <summary>MTP's exit code when no test ran, which is what listing does: success here.</summary>
    private const int ZeroTests = 8;

    public RunnerMode Mode => RunnerMode.Mtp;

    public async Task<TestListing> ListAsync(TestApp app, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        // MTP's --xunit-list has no JSON format; its "full" listing is text.
        ProcessResult mtp = await CompleteOutput.RunAsync(() => ProcessRunner.RunAsync(
            "dotnet", [app.AssemblyPath, "--xunit-list", "full", "--no-ansi", "--no-progress"], app.OutputDirectory, timeLimit, app.Environment, cancellationToken));
        if (mtp.OutputHeld)
        {
            // A text listing cut short still parses, as fewer tests: never read it.
            return TestListing.Unavailable(CompleteOutput.Problem("the test app's listing"));
        }

        return mtp.ExitCode is 0 or ZeroTests ? TestListing.Of(TestInventory.ParseText(app.AssemblyName, mtp.Output), RunnerMode.Mtp) : TestListing.NotListed;
    }

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, TraitCondition? suiteFilter, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        TestRunOutcome.Of(await NativeTestRunner.RunAsync(app, RunnerMode.Mtp, methodFilters, suiteFilter, timeLimit, time, cancellationToken));
}
