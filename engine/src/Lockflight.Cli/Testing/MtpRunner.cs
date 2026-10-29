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

    public async Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        // MTP's --xunit-list has no JSON format; its "full" listing is text.
        ProcessResult mtp = await ProcessRunner.RunAsync(
            "dotnet", [app.AssemblyPath, "--xunit-list", "full", "--no-ansi", "--no-progress"], workingDirectory, timeLimit, cancellationToken);
        return mtp.ExitCode is 0 or ZeroTests ? TestListing.Of(TestInventory.ParseText(app.AssemblyName, mtp.Output), RunnerMode.Mtp) : TestListing.NotListed;
    }

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        TestRunOutcome.Of(await NativeTestRunner.RunAsync(app, RunnerMode.Mtp, methodFilters, workingDirectory, timeLimit, time, cancellationToken));
}
