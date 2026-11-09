using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>xUnit v3's in-process console runner: <c>-list full/json</c>, then <c>-method … -result-xml …</c> in batches.</summary>
internal sealed class XunitConsoleRunner(TimeProvider time) : ITestRunner
{
    public RunnerMode Mode => RunnerMode.Native;

    public async Task<TestListing> ListAsync(TestApp app, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessResult native = await CompleteOutput.RunAsync(() =>
            ProcessRunner.RunAsync("dotnet", [app.AssemblyPath, "-list", "full/json", "-noColor"], app.OutputDirectory, timeLimit, app.Environment, cancellationToken));
        if (native.OutputHeld)
        {
            return TestListing.Unavailable(CompleteOutput.Problem("the test app's listing"));
        }

        return native.ExitCode == 0 && TestInventory.Parse(app.AssemblyName, native.Output) is IReadOnlyList<TestCase> tests
            ? TestListing.Of(tests, RunnerMode.Native)
            : TestListing.NotListed;
    }

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, TraitCondition? suiteFilter, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        TestRunOutcome.Of(await NativeTestRunner.RunAsync(app, RunnerMode.Native, methodFilters, suiteFilter, timeLimit, time, cancellationToken));
}
