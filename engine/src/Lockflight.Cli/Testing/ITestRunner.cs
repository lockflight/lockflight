namespace Lockflight.Cli.Testing;

/// <summary>Lists and runs the tests of one test application for the lifecycle (red/green, trace, review reproductions).</summary>
internal interface ITestRunner
{
    RunnerMode Mode { get; }

    /// <summary>The app's tests; <see cref="TestListing.NotListed"/> when this runner cannot list them (the probe moves on).</summary>
    Task<TestListing> ListAsync(TestApp app, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken);

    /// <summary>Runs the given methods, all when <paramref name="methodFilters"/> is null.</summary>
    Task<TestRunOutcome> RunAsync(TestApp app, IReadOnlyList<string>? methodFilters, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken);
}
