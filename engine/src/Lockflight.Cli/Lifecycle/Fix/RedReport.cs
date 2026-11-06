using Lockflight.Cli.Ci;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// What <c>lockflight red</c> prints about the tests it judged, as <c>redgreen red</c> prints it: each new test's class with why
/// (and with <c>--verbose</c> the exception, the deciding frame and the production call), a <c>dotnet test</c> line per test project
/// that runs the new tests in the person's own checkout, a GREEN_BEFORE test re-run through <c>dotnet test</c> in the worktree, and
/// the warning for tests that assert nothing.
/// </summary>
internal sealed class RedReport(TextWriter output, string checkoutRoot, TimeSpan timeLimit, TimeProvider time, bool verbose)
{
    private const string WhyIndent = "             ";

    public async Task WriteAsync(CycleJudgement cycle, TreeListing tree, string worktreeRoot, CancellationToken cancellationToken)
    {
        foreach (RedGreenTest test in cycle.Tests)
        {
            string requirements = test.Requirements.Count > 0 ? string.Join(",", test.Requirements) : "no Requirement trait";
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} ({requirements}){mark}");
            if (cycle.Verdicts.TryGetValue(test.Key, out RedVerdict? verdict))
            {
                await output.WriteLineAsync($"{WhyIndent}{verdict.Why}");
                if (verbose)
                {
                    await output.WriteLineAsync($"{WhyIndent}{verdict.Detail}");
                }
            }
        }

        HashSet<string> keys = [.. cycle.Tests.Select(test => test.Key)];
        TestCase[] fresh = [.. (tree.Inventory ?? []).Where(test => keys.Contains(test.Key))];
        IReadOnlyList<TestApp> apps = tree.Workspace.Apps;
        TestApp[] inCheckout = [.. apps.Select(app => InCheckout(app, worktreeRoot))];
        RunnerCrossCheck reproductions = new(output, timeLimit, time);
        await reproductions.WriteReproductionsAsync(inCheckout, fresh);
        await reproductions.CheckGreenBeforeAsync(apps, [.. cycle.Tests], fresh, time.GetUtcNow().Add(timeLimit), cancellationToken);
        if (RedJudge.AssertionFreeWarning(cycle.Tests) is string warning)
        {
            await output.WriteLineAsync(warning);
        }

        if (cycle.FlakyBudgetExhausted)
        {
            await output.WriteLineAsync("Note: the FLAKY check stopped early: the time budget ran out. The GREEN_BEFORE tests may also be flaky; either way they passed on this tree, so the red is rejected.");
        }
    }

    /// <summary>The app as it is in the person's checkout: the same project, so its <c>dotnet test</c> line runs there (and in an IDE).</summary>
    private TestApp InCheckout(TestApp app, string worktreeRoot) => app with
    {
        ProjectDirectory = Moved(app.ProjectDirectory, worktreeRoot),
        ProjectFile = app.ProjectFile is string file ? Moved(file, worktreeRoot) : null,
    };

    private string Moved(string path, string worktreeRoot) => Path.GetFullPath(Path.Combine(checkoutRoot, Path.GetRelativePath(worktreeRoot, path)));
}
