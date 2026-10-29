using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// MSTest through VSTest or on Microsoft.Testing.Platform: tests listed from assembly metadata (<see cref="AssemblyTestMetadata"/>),
/// run with <c>--filter "FullyQualifiedName=A|…"</c> in batches (a <c>[DataRow]</c> or <c>[DynamicData]</c> row shares its method's
/// name) and read from TRX. MSTest prints no exception type for an assertion; <see cref="TrxFailureMessages.MSTest"/> recognises its wording.
/// </summary>
internal sealed class MSTestRunner(TimeProvider time, TestRunnerKind platform) : ITestRunner
{
    public RunnerMode Mode => platform == TestRunnerKind.MicrosoftTestingPlatform ? RunnerMode.MSTestMtp : RunnerMode.MSTest;

    public Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        Task.FromResult(TrxTestListing.List(app, platform, TestFrameworkKind.MSTest, Mode));

    public async Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        TestRunOutcome.Of(await TrxTestRun.RunAsync(
            app, platform, VSTestRunner.FilterBatches(methodFilters), TrxFailureMessages.MSTest, workingDirectory, timeLimit, time, cancellationToken));
}
