namespace Lockflight.Cli.Testing;

/// <summary>
/// Lists and runs the tests of one test application for the lifecycle (red/green, trace, review reproductions), as <c>dotnet test</c>
/// starts it: in its output folder, with the variables of its run settings (<see cref="TestApp.OutputDirectory"/>, <see cref="TestApp.Environment"/>).
/// </summary>
internal interface ITestRunner
{
    RunnerMode Mode { get; }

    /// <summary>The app's tests; <see cref="TestListing.NotListed"/> when this runner cannot list them (the probe moves on).</summary>
    Task<TestListing> ListAsync(TestApp app, TimeSpan timeLimit, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the given methods, all when <paramref name="methodFilters"/> is null; a whole-suite run leaves out the tests
    /// <paramref name="suiteFilter"/> excludes (ignored when methods are given).
    /// </summary>
    Task<TestRunOutcome> RunAsync(
        TestApp app, IReadOnlyList<string>? methodFilters, TraitCondition? suiteFilter, TimeSpan timeLimit, CancellationToken cancellationToken);
}
