using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What a red prints so its run can be compared with a plain <c>dotnet test</c>: for each test application, the <c>dotnet test</c>
/// line that runs the red's new tests; and for a GREEN_BEFORE test, one re-run through <c>dotnet test</c> within the red's time
/// budget. When that run fails the test, a warning says so with both command lines, working directories and the environment that
/// commonly matters. The verdict never changes: a test that passed under the red's own run passed before the implementation existed,
/// so the red stays rejected.
/// </summary>
internal sealed class RunnerCrossCheck(TextWriter output, TimeSpan timeLimit, TimeProvider time)
{
    /// <summary>GREEN_BEFORE tests re-run through <c>dotnet test</c> per red, each a run of its own.</summary>
    public const int MaxChecks = 3;

    private const string Indent = "  ";

    /// <summary>One <c>dotnet test</c> line per app that holds any of <paramref name="tests"/>, filtered to them.</summary>
    public async Task WriteReproductionsAsync(IReadOnlyList<TestApp> apps, IReadOnlyList<TestCase> tests)
    {
        foreach (TestApp app in apps)
        {
            string[] filters = FiltersIn(app, tests);
            if (filters.Length > 0 && TestCommands.DotnetTest(app, filters, noBuild: false) is TestCommand command)
            {
                await output.WriteLineAsync($"Reproduce {app.AssemblyName} with dotnet test: {command.Line}");
            }
        }
    }

    /// <summary>
    /// Re-runs up to <see cref="MaxChecks"/> GREEN_BEFORE tests through <c>dotnet test</c>, each before <paramref name="deadline"/>, and
    /// warns for each that fails there; says so when a re-run could not tell, or was not made.
    /// </summary>
    public async Task CheckGreenBeforeAsync(
        IReadOnlyList<TestApp> apps, IReadOnlyList<RedGreenTest> classified, IReadOnlyList<TestCase> tests, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        RedGreenTest[] greenBefore = [.. classified.Where(test => test.RedClass == RedClass.GreenBefore)];
        int checkedCount = 0;
        foreach (RedGreenTest test in greenBefore)
        {
            if (checkedCount == MaxChecks)
            {
                break;
            }

            if (tests.FirstOrDefault(candidate => candidate.Key == test.Key) is not TestCase testCase
                || apps.FirstOrDefault(app => app.AssemblyName == testCase.Assembly) is not TestApp app
                || TestCommands.DotnetTest(app, [testCase.MethodFilter], noBuild: true) is not TestCommand dotnetTest)
            {
                continue;
            }

            TimeSpan left = deadline - time.GetUtcNow();
            if (left <= TimeSpan.Zero)
            {
                await output.WriteLineAsync($"Note: the red's time budget ran out before {testCase.MethodFilter} could be re-run through dotnet test.");
                return;
            }

            checkedCount++;
            await output.WriteLineAsync($"Re-running {testCase.MethodFilter} through dotnet test to compare: {dotnetTest.Line}");
            DotnetTestOutcome outcome = await DotnetTestCheck.RunAsync(dotnetTest, TestRunnerDetection.For(app.ProjectDirectory), left < timeLimit ? left : timeLimit, cancellationToken);
            if (outcome == DotnetTestOutcome.Failed)
            {
                await output.WriteLineAsync(Warning(app, testCase.MethodFilter, FiltersIn(app, tests), dotnetTest, RunnerEnvironment.Inherited()));
            }
            else if (outcome == DotnetTestOutcome.Inconclusive)
            {
                await output.WriteLineAsync($"Note: dotnet test did not say whether {testCase.MethodFilter} passes (it timed out, broke or ran no test); run the line above to see.");
            }
        }

        if (greenBefore.Length > checkedCount && checkedCount == MaxChecks)
        {
            await output.WriteLineAsync($"Note: {checkedCount} of {greenBefore.Length} GREEN_BEFORE tests were re-run through dotnet test.");
        }
    }

    /// <summary>
    /// The warning for a test that passed under the red's run and failed under <c>dotnet test</c>: the red's run as it was made (every
    /// new test of the app, <paramref name="redFilters"/>), and the <c>dotnet test</c> run of that test alone.
    /// </summary>
    public static string Warning(TestApp app, string methodFilter, IReadOnlyList<string> redFilters, TestCommand dotnetTest, IReadOnlyDictionary<string, string?> inherited)
    {
        TestCommand engineRun = TestCommands.EngineRun(app, redFilters);
        string runSettings = app.Environment.Count == 0 ? string.Empty : $" (and the variables of {app.RunSettingsFile}, which dotnet test applies itself)";
        return string.Join(Environment.NewLine,
            $"Warning: {methodFilter} passes under red's run but fails under dotnet test — the difference is the runner environment (the working "
            + "directory, environment variables, how the test process is started, or the other new tests red ran with it), unless the test "
            + "is flaky. The verdict stays GREEN_BEFORE: the test passed before the implementation existed under red's own run. Find what "
            + "the test depends on and make it explicit in the test.",
            $"{Indent}red's run:   {engineRun.Line}",
            $"{Indent}             environment: {RunnerEnvironment.Describe(inherited, ProcessRunner.EngineVariables, app.Environment)}",
            $"{Indent}dotnet test: {dotnetTest.Line}",
            $"{Indent}             environment: {RunnerEnvironment.Describe(inherited, ProcessRunner.EngineVariables)}{runSettings}");
    }

    private static string[] FiltersIn(TestApp app, IReadOnlyList<TestCase> tests) =>
        [.. tests.Where(test => test.Assembly == app.AssemblyName).Select(test => test.MethodFilter)];
}
