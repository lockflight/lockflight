using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Step P1: builds and lists the head with the base policy, runs the whole suite once, and runs the tests that failed again, up to
/// <c>ci.flakyRetries</c> times, so a flake is told from a failure. Known-failing tests come from the baselines the base policy laid
/// over the tree.
/// </summary>
internal static class HeadRunner
{
    public static async Task<HeadRun> RunAsync(TreeBuilder trees, JudgePlan plan, CancellationToken cancellationToken)
    {
        TreeListing listing = await trees.ListAsync(plan.Inputs.Head, "head", fresh: true, cancellationToken);
        IReadOnlySet<string> knownFailing = plan.Policy.Manifest is Manifest manifest
            ? KnownFailingTests.LoadAll(manifest with { Root = trees.Root })
            : new HashSet<string>(StringComparer.Ordinal);
        if (listing.Inventory is null)
        {
            return new HeadRun(listing, null, new HashSet<string>(StringComparer.Ordinal), null, knownFailing);
        }

        TestWorkspace workspace = listing.Workspace;
        if (await workspace.RunAsync(null, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return new HeadRun(listing, null, new HashSet<string>(StringComparer.Ordinal), workspace.AbnormalExit, knownFailing);
        }

        string? abnormal = workspace.AbnormalExit;
        List<ExecutedTest> merged = [.. results];
        HashSet<string> passedOnRetry = new(StringComparer.Ordinal);
        for (int retry = 0; retry < plan.Policy.Ci.FlakyRetries; retry++)
        {
            string[] failing = [.. merged.Where(result => result.Outcome == TestOutcome.Fail && !knownFailing.Contains(result.Name)).Select(result => result.Key).Distinct(StringComparer.Ordinal)];
            TestCase[] again = [.. listing.Inventory.Where(test => failing.Contains(test.Key, StringComparer.Ordinal))];
            if (again.Length == 0 || await workspace.RunAsync(again, cancellationToken) is not IReadOnlyList<ExecutedTest> rerun)
            {
                break;
            }

            foreach (TestCase test in again.Where(test => SuiteChecks.PassedEveryRow(rerun, test.Key)))
            {
                passedOnRetry.Add(test.Key);
                merged.RemoveAll(result => result.Key == test.Key);
                merged.AddRange(rerun.Where(result => result.Key == test.Key));
            }
        }

        return new HeadRun(listing, merged, passedOnRetry, abnormal, knownFailing);
    }
}
