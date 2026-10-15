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

    /// <summary>The test application whose listing or run failed last, for the error message.</summary>
    public string? FailedApp { get; private set; }

    /// <summary>
    /// After <see cref="RunAsync(IReadOnlyList{TestCase}?, CancellationToken)"/>: the test application whose runner ended with an exit
    /// code that is not a test verdict (crash, timeout), with that code; null when every runner finished normally.
    /// </summary>
    public string? AbnormalExit { get; private set; }

    /// <summary>Builds; returns the build output when the build fails, null on success.</summary>
    public async Task<string?> BuildAsync(CancellationToken cancellationToken)
    {
        List<TestApp> apps = [];
        foreach (BuildTarget target in targets)
        {
            ProcessResult build = await ProcessRunner.RunAsync(
                "dotnet", ["build", .. target.BuildFile is null ? [] : new[] { target.BuildFile }, "--nologo", "--interactive", "false"], target.Directory, timeLimit, cancellationToken);
            if (build.ExitCode != 0)
            {
                return build.Combined;
            }

            apps.AddRange(await TestApps.FindAsync(target.Directory, target.PreferredFramework, cancellationToken));
        }

        Apps = apps;
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
