using System.Diagnostics;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Builds one or more directories (a repository, or the units of a workspace) and lists and runs the tests of all their xUnit test
/// applications; paths are reported relative to <paramref name="root"/>.
/// </summary>
internal sealed class TestWorkspace(string root, IReadOnlyList<BuildTarget> targets, TimeSpan timeLimit)
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
        new(otherRoot, [.. targets.Select(target => target with { Directory = Path.GetFullPath(Path.Combine(otherRoot, Path.GetRelativePath(root, target.Directory))) })], timeLimit);

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

            TestAppDiscovery discovery = await TestApps.FindAsync(target.Directory, target.PreferredFramework, timeLimit, cancellationToken);
            apps.AddRange(discovery.Apps);
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
            if (await TestInventory.ListAsync(app, root, timeLimit, cancellationToken) is not (IReadOnlyList<TestCase> tests, RunnerMode mode))
            {
                FailedApp = Path.GetRelativePath(root, app.AssemblyPath);
                return null;
            }

            all.AddRange(tests);
            probed.Add(app with { Mode = mode });
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
    public async Task<IReadOnlyList<ExecutedTest>?> RunAsync(IReadOnlyList<TestCase>? tests, TimeSpan budget, CancellationToken cancellationToken)
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

            if (await NativeTestRunner.RunAsync(app, filters, root, left < timeLimit ? left : timeLimit, cancellationToken) is not TestRunReport report)
            {
                FailedApp = Path.GetRelativePath(root, app.AssemblyPath);
                return null;
            }

            if (report.AbnormalExitCode is int exitCode)
            {
                AbnormalExit ??= $"{Path.GetRelativePath(root, app.AssemblyPath)} (runner exit code {exitCode})";
            }

            results.AddRange(report.Results);
        }

        return results;
    }
}
