using System.Diagnostics;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Builds one or more directories (a repository, or the units of a workspace) and lists and runs the tests of all their xUnit, NUnit
/// and MSTest test applications; paths are reported relative to <paramref name="root"/>.
/// </summary>
internal sealed class TestWorkspace(string root, IReadOnlyList<BuildTarget> targets, TimeSpan timeLimit, TimeProvider time)
{
    public IReadOnlyList<TestApp> Apps { get; private set; } = [];

    /// <summary>After a build: the projects whose evaluation failed or timed out, so it is unknown whether <see cref="Apps"/> is complete.</summary>
    public IReadOnlyList<UnevaluatedProject> Unevaluated { get; private set; } = [];

    /// <summary>After a build: why the test applications found may be incomplete, with what to do; null when every project was evaluated.</summary>
    public string? DiscoveryProblem => Unevaluated.Count == 0 ? null : UnevaluatedProjects.Describe(root, Unevaluated, timeLimit);

    /// <summary>The test application whose listing or run failed last, for the error message.</summary>
    public string? FailedApp { get; private set; }

    /// <summary>
    /// After <see cref="RunAsync(IReadOnlyList{TestCase}?, CancellationToken)"/>: the test application whose runner ended with an exit
    /// code that is not a test verdict (crash, timeout), with that code; null when every runner finished normally.
    /// </summary>
    public string? AbnormalExit { get; private set; }

    /// <summary>The directories this workspace builds.</summary>
    public IReadOnlyList<string> Directories => [.. targets.Select(target => target.Directory)];

    /// <summary>
    /// The same build targets at the same places under <paramref name="otherRoot"/> (a worktree of the project), with nothing built yet.
    /// </summary>
    public TestWorkspace MovedTo(string otherRoot) =>
        new(otherRoot, [.. targets.Select(target => target with { Directory = Path.GetFullPath(Path.Combine(otherRoot, Path.GetRelativePath(root, target.Directory))) })], timeLimit, time);

    /// <summary>
    /// Builds and finds the test applications; returns the build output when the build fails, null on success (then check
    /// <see cref="DiscoveryProblem"/>: a project that could not be evaluated may be a test project left out).
    /// </summary>
    public async Task<string?> BuildAsync(CancellationToken cancellationToken)
    {
        List<TestApp> apps = [];
        List<UnevaluatedProject> unevaluated = [];
        foreach (BuildTarget target in targets)
        {
            ProcessResult build = await ProcessRunner.RunAsync(
                "dotnet", ["build", .. target.BuildFile is null ? [] : new[] { target.BuildFile }, "--nologo", "--interactive", "false"], target.Directory, timeLimit, cancellationToken);
            if (build.ExitCode != 0)
            {
                return build.Combined;
            }

            TestAppDiscovery discovery = await TestApps.FindAsync(target.Directory, target.PreferredFramework, timeLimit, time, cancellationToken);
            apps.AddRange(discovery.Apps.Select(app => app with { StopFilter = target.StopFilter }));
            unevaluated.AddRange(discovery.Unevaluated);
        }

        Apps = apps;
        Unevaluated = unevaluated;
        return null;
    }

    public async Task<IReadOnlyList<TestCase>?> ListAsync(CancellationToken cancellationToken)
    {
        List<TestCase> all = [];
        List<TestApp> probed = [];
        foreach (TestApp app in Apps)
        {
            TestListing listing = await TestRunners.ListAsync(app, root, timeLimit, time, cancellationToken);
            if (!listing.Listed)
            {
                FailedApp = listing.Problem is null ? Path.GetRelativePath(root, app.AssemblyPath) : $"{Path.GetRelativePath(root, app.AssemblyPath)} ({listing.Problem})";
                return null;
            }

            all.AddRange(listing.Tests);
            probed.Add(app with { Mode = listing.Mode });
        }

        Apps = probed;
        return all;
    }

    /// <summary>Runs the given tests (all when null) in every app that holds any of them; call <see cref="ListAsync"/> first (it settles each app's runner mode).</summary>
    public Task<IReadOnlyList<ExecutedTest>?> RunAsync(IReadOnlyList<TestCase>? tests, CancellationToken cancellationToken) =>
        RunAsync(tests, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Runs the given tests within <paramref name="budget"/> for all apps together, on top of the per-process time limit
    /// (<see cref="Timeout.InfiniteTimeSpan"/>: the per-process limit only); null when the budget runs out.
    /// </summary>
    public Task<IReadOnlyList<ExecutedTest>?> RunAsync(IReadOnlyList<TestCase>? tests, TimeSpan budget, CancellationToken cancellationToken) =>
        RunAsync(tests, budget, suiteFilters: null, cancellationToken);

    /// <summary>
    /// Red/green's whole-suite regression pass: every app's tests but those its <c>test.stopFilter</c> leaves out, which
    /// <paramref name="inventory"/> (the listing) names, so they are known as left out rather than missing. A filter that is not one
    /// trait condition is not applied: the app runs every test, and the run says so. Null when a runner could not run.
    /// </summary>
    public async Task<RegressionRun?> RunRegressionAsync(IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        Dictionary<TestApp, TraitCondition> conditions = [];
        List<string> unsupported = [];
        foreach (TestApp app in Apps.Where(app => app.StopFilter is not null))
        {
            if (TraitCondition.Parse(app.StopFilter!) is TraitCondition condition)
            {
                conditions[app] = condition;
            }
            else
            {
                unsupported.Add(app.StopFilter!);
            }
        }

        if (await RunAsync(null, Timeout.InfiniteTimeSpan, conditions, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return null;
        }

        string[] excluded = [.. Apps.Where(conditions.ContainsKey)
            .SelectMany(app => inventory.Where(test => test.Assembly == app.AssemblyName && conditions[app].Excludes(test)))
            .Select(test => test.Key)
            .Distinct(StringComparer.Ordinal)];
        string[] applied = [.. Apps.Where(conditions.ContainsKey).Select(app => app.StopFilter!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new RegressionRun(
            results,
            excluded.ToHashSet(StringComparer.Ordinal),
            applied.Length == 0 ? null : new RegressionFilter(applied, excluded.Length) { ExcludedTests = [.. excluded.Order(StringComparer.Ordinal)] },
            [.. unsupported.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    private async Task<IReadOnlyList<ExecutedTest>?> RunAsync(
        IReadOnlyList<TestCase>? tests, TimeSpan budget, IReadOnlyDictionary<TestApp, TraitCondition>? suiteFilters, CancellationToken cancellationToken)
    {
        List<ExecutedTest> results = [];
        AbnormalExit = null;
        Stopwatch elapsed = Stopwatch.StartNew();
        foreach (TestApp app in Apps)
        {
            string[]? filters = tests is null ? null : [.. tests.Where(test => test.Assembly == app.AssemblyName).Select(test => test.MethodFilter)];
            if (filters is { Length: 0 })
            {
                continue;
            }

            TimeSpan left = budget == Timeout.InfiniteTimeSpan ? timeLimit : budget - elapsed.Elapsed;
            if (left <= TimeSpan.Zero)
            {
                FailedApp = Path.GetRelativePath(root, app.AssemblyPath);
                return null;
            }

            TraitCondition? suiteFilter = suiteFilters?.GetValueOrDefault(app);
            TestRunOutcome run = await TestRunners.For(app.Mode, time).RunAsync(app, filters, suiteFilter, root, left < timeLimit ? left : timeLimit, cancellationToken);
            if (!run.Reported)
            {
                FailedApp = Path.GetRelativePath(root, app.AssemblyPath);
                return null;
            }

            TestRunReport report = run.Report;

            if (report.AbnormalExitCode is int exitCode)
            {
                AbnormalExit ??= $"{Path.GetRelativePath(root, app.AssemblyPath)} (runner exit code {exitCode})";
            }

            results.AddRange(report.Results);
        }

        return results;
    }
}
