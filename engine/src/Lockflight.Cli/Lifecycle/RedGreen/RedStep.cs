using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen red</c>: accepts the new tests of a cycle when they all fail in production code (RED_STUB, or RED_ASSERT
/// in a test that calls production code), which opens the cycle and locks the test projects. A new test whose code asserts nothing
/// is flagged assertion-free but not rejected: a test may verify by letting the production code throw (a guard clause, a
/// validator), which a static reading of the test code cannot tell from a test that verifies nothing. An accepted red keeps a snapshot
/// of the tree (<see cref="RedSnapshots"/>). While the open cycle has an amend waiting, a red re-checks only the amended tests instead,
/// on that snapshot (<see cref="AmendRecheck"/>).
/// </summary>
/// <remarks>Each test's line is followed by why it got its class; <paramref name="verbose"/> adds the exception, the deciding stack
/// frame with its source line, and the production call found.</remarks>
internal sealed class RedStep(RedGreenSession session, bool verbose)
{
    private readonly RedJudge judge = new(session, verbose);

    public async Task<ExitCode> RunAsync(RedGreenRecord? record, CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return await session.FailAsync($"no baseline; run '{EngineCommand.Linked} redgreen start' before writing the feature's tests", cancellationToken);
        }

        if (record.OpenCycle is RedGreenCycle open && !open.OpenAmends.Any())
        {
            return await session.FailAsync($"cycle {open.Number} is still red; make it green ('{EngineCommand.Linked} redgreen green') before the next red", cancellationToken);
        }

        if (session.UnapprovedArtifacts() is string unapproved)
        {
            return await session.FailAsync(unapproved, cancellationToken);
        }

        // The FLAKY re-runs must end within gates.timeoutSeconds of the red's start (the build, listing and first run count against
        // it), so a big cycle cannot run for ages; each of those earlier steps is bounded by the per-process limit as before.
        DateTimeOffset deadline = session.Context.Time.GetUtcNow().AddSeconds(session.Manifest.Gates.TimeoutSeconds);
        TestWorkspace workspace = session.Workspace;

        // Taken before the build, so the tree is the one this red judges, not one changed while it ran.
        RedSnapshot snapshot = record.OpenCycle is null
            ? await RedSnapshots.TakeAsync(session.Manifest.Root, workspace.Directories, TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds), cancellationToken)
            : RedSnapshot.Skipped("a re-check keeps the cycle's own snapshot");
        IReadOnlyDictionary<string, string> testFilesAtStart = TestFileHashes.Compute(session.Manifest.Root, record.TestProjects);
        if (await workspace.BuildAsync(cancellationToken) is string buildOutput)
        {
            await session.Output.WriteLineAsync($"COMPILE_RED the tests do not compile; declare the members they use as stubs ('throw new NotImplementedException();').\n{RedGreenSession.Tail(buildOutput)}");
            await session.RecordAsync(LedgerSignatures.RedCompileError, session.Feature.Id, cancellationToken);
            return ExitCode.Fail;
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            return await session.BrokenAsync($"the test runner could not list the tests of {workspace.FailedApp}", cancellationToken);
        }

        if (record.OpenCycle is RedGreenCycle amending)
        {
            return await new AmendRecheck(session, judge).RunAsync(record, amending, inventory, deadline, cancellationToken);
        }

        if (await NewTestsAsync(record, inventory, cancellationToken) is not TestCase[] fresh)
        {
            return ExitCode.Fail;
        }

        if (await workspace.RunAsync(fresh, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return await session.BrokenAsync($"the test runner produced no report for {workspace.FailedApp}", cancellationToken);
        }

        return await JudgeAsync(record, inventory, fresh, results, snapshot, testFilesAtStart, deadline, cancellationToken);
    }

    /// <summary>The tests added since the baseline and earlier cycles; null — after saying why — when the suite lost or weakened tests, or nothing is new.</summary>
    private async Task<TestCase[]?> NewTestsAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        if (await judge.SuiteProblemAsync(record, inventory, cancellationToken))
        {
            return null;
        }

        IReadOnlySet<string> known = record.KnownTests;
        TestCase[] fresh = [.. inventory.Where(test => !known.Contains(test.Key))];
        if (fresh.Length == 0)
        {
            await session.FailAsync("no new tests since the baseline and earlier cycles; write the story's tests first", cancellationToken);
            return null;
        }

        return fresh;
    }

    private async Task<ExitCode> JudgeAsync(
        RedGreenRecord record,
        IReadOnlyList<TestCase> inventory,
        TestCase[] fresh,
        IReadOnlyList<ExecutedTest> results,
        RedSnapshot snapshot,
        IReadOnlyDictionary<string, string> testFilesAtStart,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        (List<RedGreenTest> classified, FlakyCheckResult flakyCheck, IReadOnlyDictionary<string, RedVerdict> verdicts) =
            await judge.ClassifyAsync(session.Workspace, session.Manifest.Root, inventory, fresh, results, deadline, cancellationToken);
        foreach (RedGreenTest test in classified)
        {
            string requirements = test.Requirements.Count > 0 ? string.Join(",", test.Requirements) : "no Requirement trait";
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await session.Output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} ({requirements}){mark}");
            await judge.WriteWhyAsync(verdicts[test.Key]);
        }

        await judge.WarnAssertionFreeAsync(classified);

        if (flakyCheck.BudgetExhausted)
        {
            await session.Output.WriteLineAsync(
                $"Note: the FLAKY check stopped after {flakyCheck.ReRuns} of {FlakyCheck.MaxReRuns} re-runs — the red's time budget (gates.timeoutSeconds = {session.Manifest.Gates.TimeoutSeconds} s) ran out. " +
                "The GREEN_BEFORE tests may also be flaky; either way they passed before the implementation existed, so the red is rejected.");
        }

        RedGreenTest[] rejected = [.. classified.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        if (rejected.Length > 0)
        {
            await session.Output.WriteLineAsync($"Red rejected: {rejected.Length} of {classified.Count} new tests do not fail for the right reason. A red must be RED_STUB or RED_ASSERT.");
            foreach (RedClass redClass in rejected.Select(test => test.RedClass).Distinct())
            {
                await session.Output.WriteLineAsync($"  {RedClassNames.ToName(redClass)}: {RedClassFixes.For(redClass)}.");
            }

            await session.RecordAsync(LedgerSignatures.RedRejected, $"{session.Feature.Id}: {string.Join(",", rejected.Select(test => RedClassNames.ToName(test.RedClass)).Distinct())}", cancellationToken);
            return ExitCode.Fail;
        }

        return await AcceptAsync(record, classified, snapshot, testFilesAtStart, cancellationToken);
    }

    private async Task<ExitCode> AcceptAsync(
        RedGreenRecord record, List<RedGreenTest> classified, RedSnapshot taken, IReadOnlyDictionary<string, string> testFilesAtStart, CancellationToken cancellationToken)
    {
        // The files this red judged are the ones it locks: a test file changed while it ran was not judged.
        if (TestFileHashes.Compare(testFilesAtStart, TestFileHashes.Compute(session.Manifest.Root, record.TestProjects)) is { Any: true } changedMeanwhile)
        {
            return await session.FailAsync(
                $"{changedMeanwhile.Describe()} changed while the red ran, so this red did not judge that content; run '{EngineCommand.Linked} redgreen red' again",
                cancellationToken);
        }

        int number = record.Cycles.Count + 1;
        RedSnapshot snapshot = taken.Commit is string commit
            && await RedSnapshots.KeepAsync(session.Manifest.Root, session.Feature.Id, number, commit, TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds), cancellationToken) is null
            ? RedSnapshot.Skipped($"git could not keep the snapshot under {RedSnapshots.RefFor(session.Feature.Id, number, commit)}")
            : taken;

        // Test projects added since the start are locked too.
        string[] testProjects = [.. record.TestProjects.Union(session.RelativeProjects(), StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        RedGreenCycle cycle = new(number, session.Context.Time.GetUtcNow(), GreenAt: null, classified)
        {
            Snapshot = snapshot.Commit,
            TestFiles = TestFileHashes.Compute(session.Manifest.Root, testProjects),
        };
        RedGreenRecord accepted = record with { TestProjects = testProjects, Cycles = [.. record.Cycles, cycle] };
        session.Save(accepted);
        await session.RecordAsync(LedgerSignatures.RedAccepted, $"{session.Feature.Id}: cycle {cycle.Number}, {classified.Count} tests", cancellationToken);
        await session.Output.WriteLineAsync($"Red accepted: cycle {cycle.Number} with {classified.Count} tests. The test projects ({string.Join(", ", accepted.TestProjects)}) are locked until '{EngineCommand.Linked} redgreen green' passes.");
        if (classified.Any(test => test.Requirements.Count == 0))
        {
            await session.Output.WriteLineAsync("Note: tests without [Trait(\"Requirement\", \"FR-xxx\")] will not count for trace.");
        }

        await DescribeAsync(snapshot, number);
        return ExitCode.Pass;
    }

    private async Task DescribeAsync(RedSnapshot snapshot, int cycle)
    {
        if (snapshot.Commit is not string commit)
        {
            await session.Output.WriteLineAsync($"Note: no red-time snapshot ({snapshot.NotTaken}); an amend of this cycle would be re-checked on the current tree.");
            return;
        }

        await session.Output.WriteLineAsync(
            $"Red-time snapshot: {RedGreenSession.Short(commit)} ({RedSnapshots.RefFor(session.Feature.Id, cycle, commit)}, local only); an amend of this cycle is re-checked against it.");
        if (snapshot.UntrackedNonSource.Count > 0)
        {
            await session.Output.WriteLineAsync(
                $"Note: the snapshot also holds untracked files that do not look like source: {string.Join(", ", snapshot.UntrackedNonSource.Take(RedGreenSession.MaxTestsListed))}"
                + $"{(snapshot.UntrackedNonSource.Count > RedGreenSession.MaxTestsListed ? $" and {snapshot.UntrackedNonSource.Count - RedGreenSession.MaxTestsListed} more" : string.Empty)}. "
                + "They are in the repository's local object store now (nothing pushes the snapshot); add them to .gitignore if they should stay out of git.");
        }
    }
}
