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
/// on that snapshot (<see cref="RecheckAsync"/>).
/// </summary>
/// <remarks>Each test's line is followed by why it got its class; <paramref name="verbose"/> adds the exception, the deciding stack
/// frame with its source line, and the production call found.</remarks>
internal sealed class RedStep(RedGreenSession session, bool verbose)
{
    /// <summary>Indents the why lines under the test's name, past the class column.</summary>
    private const string WhyIndent = "             ";

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
            return await RecheckAsync(record, amending, inventory, deadline, cancellationToken);
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

    /// <summary>
    /// The red after an amend: the amended tests must fail in production code again (RED_STUB or RED_ASSERT), with nothing lost or
    /// added in the suite. They are run on the cycle's red-time snapshot with the amended files laid over it, so an implementation
    /// written since the red cannot make them pass; a cycle without a snapshot re-checks them on the current tree. Then their new
    /// classification replaces the old one and their files are locked again; the rest of the cycle's evidence is kept. Otherwise the
    /// amend stays open, and its files writable.
    /// </summary>
    private async Task<ExitCode> RecheckAsync(
        RedGreenRecord record, RedGreenCycle open, IReadOnlyList<TestCase> inventory, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (await SuiteProblemAsync(record, inventory, cancellationToken))
        {
            return ExitCode.Fail;
        }

        IReadOnlySet<string> known = record.KnownTests;
        string[] added = [.. inventory.Where(test => !known.Contains(test.Key)).Select(test => test.MethodFilter).Order(StringComparer.Ordinal)];
        if (added.Length > 0)
        {
            return await session.FailAsync(
                $"new tests appeared while an amend is open: {string.Join(", ", added.Take(RedGreenSession.MaxTestsListed))}. A red after an amend re-checks only the amended tests; "
                + "remove the new ones (the amended file is still writable) and add them in the next cycle",
                cancellationToken);
        }

        HashSet<string> amendedKeys = [.. open.OpenAmends.Select(amend => amend.TestKey)];
        RecheckedFiles files = new(session.Manifest.Root, open);
        if (open.Snapshot is string snapshot)
        {
            return await RecheckOnSnapshotAsync(record, open, snapshot, amendedKeys, files, deadline, cancellationToken);
        }

        await session.Output.WriteLineAsync(
            $"Note: cycle {open.Number} has no red-time snapshot, so the amended tests are re-checked on the current tree: an implementation written since the red makes them pass (GREEN_BEFORE). "
            + $"If it does, a person can move the implementation aside for this red, or discard the cycle with '{EngineCommand.Linked} redgreen reset'.");
        return await JudgeRecheckAsync(
            record, open, session.Workspace, session.Manifest.Root, inventory, [.. inventory.Where(test => amendedKeys.Contains(test.Key))], files, deadline, cancellationToken);
    }

    /// <summary>
    /// Builds the red-time snapshot in a temporary worktree, with the current content of every file amended in the cycle laid over it,
    /// and re-checks the amended tests there. Anything that goes wrong refuses the red, so the amend stays open; the worktree is always
    /// removed.
    /// </summary>
    private async Task<ExitCode> RecheckOnSnapshotAsync(
        RedGreenRecord record,
        RedGreenCycle open,
        string snapshot,
        HashSet<string> amendedKeys,
        RecheckedFiles files,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        string root = session.Manifest.Root;
        TimeSpan timeLimit = TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds);
        await session.Output.WriteLineAsync(
            $"Re-checking the amended tests on the red-time snapshot {RedGreenSession.Short(snapshot)} of cycle {open.Number} ({RedSnapshots.RefFor(session.Feature.Id, open.Number, snapshot)}), with the amended files laid over it…");
        SnapshotWorktreeOpening opening = await SnapshotWorktree.OpenAsync(root, snapshot, timeLimit, cancellationToken);
        if (opening.Worktree is not SnapshotWorktree worktree)
        {
            return await session.BrokenAsync($"the amended tests cannot be re-checked: {opening.Problem}. The amend stays open; a person can discard the cycle with '{EngineCommand.Linked} redgreen reset'", cancellationToken);
        }

        try
        {
            if (Overlay(root, worktree.Root, open.Amends.SelectMany(amend => amend.Files).Distinct(StringComparer.Ordinal)) is string notCopied)
            {
                return await session.BrokenAsync($"the amended files could not be laid over the red-time snapshot: {notCopied}", cancellationToken);
            }

            TestWorkspace workspace = session.Workspace.MovedTo(worktree.Root);
            if (await workspace.BuildAsync(cancellationToken) is string buildOutput)
            {
                await session.RecordAsync(LedgerSignatures.RedRejected, $"{session.Feature.Id}: amended tests do not build on the red-time snapshot", cancellationToken);
                return await session.FailAsync(
                    "the amended tests do not build against the code as it was at red. An amended test must compile against the red-time code (its stubs); "
                    + "one that needs an API added after the red, or a file git ignores (not in the snapshot), needs a person, who can discard the cycle with "
                    + $"'{EngineCommand.Linked} redgreen reset'. The amend stays open.\n{RedGreenSession.Tail(buildOutput)}",
                    cancellationToken);
            }

            if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
            {
                return await session.BrokenAsync($"the test runner could not list the tests of the red-time snapshot ({workspace.FailedApp})", cancellationToken);
            }

            TestCase[] amended = [.. inventory.Where(test => amendedKeys.Contains(test.Key))];
            if (amended.Length < amendedKeys.Count)
            {
                string[] missing = [.. open.OpenAmends.Where(amend => !amended.Any(test => test.Key == amend.TestKey)).Select(amend => amend.MethodFilter)];
                return await session.FailAsync(
                    $"{string.Join(", ", missing)} {(missing.Length == 1 ? "is" : "are")} not among the tests of the red-time snapshot with the amended files laid over it; "
                    + "an amend changes a test, it does not rename or move it. The amend stays open",
                    cancellationToken);
            }

            return await JudgeRecheckAsync(record, open, workspace, worktree.Root, inventory, amended, files, deadline, cancellationToken);
        }
        finally
        {
            if (await worktree.RemoveAsync() is string leftBehind)
            {
                await session.Output.WriteLineAsync($"Warning: {leftBehind}.");
            }
        }
    }

    /// <summary>
    /// Lays the test files' current content over the worktree, and deletes there a file deleted since the red; null when that worked,
    /// else what failed.
    /// </summary>
    private static string? Overlay(string root, string worktreeRoot, IEnumerable<string> files)
    {
        try
        {
            foreach (string file in files)
            {
                string current = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
                string target = Path.Combine(worktreeRoot, file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(current))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(current, target, overwrite: true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    /// <summary>Runs and classifies the amended tests in <paramref name="workspace"/> (the project, or a worktree of its snapshot) and records the verdict.</summary>
    private async Task<ExitCode> JudgeRecheckAsync(
        RedGreenRecord record,
        RedGreenCycle open,
        TestWorkspace workspace,
        string root,
        IReadOnlyList<TestCase> inventory,
        TestCase[] amended,
        RecheckedFiles files,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        if (await workspace.RunAsync(amended, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
        {
            return await session.BrokenAsync($"the test runner produced no report for {workspace.FailedApp}", cancellationToken);
        }

        (List<RedGreenTest> classified, _, IReadOnlyDictionary<string, RedVerdict> verdicts) =
            await ClassifyAsync(workspace, root, inventory, amended, results, deadline, cancellationToken);
        foreach (RedGreenTest test in classified)
        {
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await session.Output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} (amended){mark}");
            await WriteWhyAsync(verdicts[test.Key]);
        }

        await WarnAssertionFreeAsync(classified);
        RedGreenTest[] rejected = [.. classified.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        if (rejected.Length > 0)
        {
            await session.Output.WriteLineAsync(
                $"Red rejected: {rejected.Length} of {classified.Count} amended tests do not fail in production code{(open.Snapshot is null ? string.Empty : " as it was at red")}. The amend stays open and the file writable until they do.");
            foreach (RedClass redClass in rejected.Select(test => test.RedClass).Distinct())
            {
                await session.Output.WriteLineAsync($"  {RedClassNames.ToName(redClass)}: {RedClassFixes.For(redClass)}.");
            }

            await session.RecordAsync(LedgerSignatures.RedRejected, $"{session.Feature.Id}: amended tests, {string.Join(",", rejected.Select(test => RedClassNames.ToName(test.RedClass)).Distinct())}", cancellationToken);
            return ExitCode.Fail;
        }

        if (files.ChangedSince() is { Count: > 0 } changedMeanwhile)
        {
            return await session.FailAsync(
                $"{string.Join(", ", changedMeanwhile)} changed while the amended tests were re-checked; run '{EngineCommand.Linked} redgreen red' again. The amend stays open",
                cancellationToken);
        }

        Dictionary<string, RedGreenTest> replaced = classified.ToDictionary(test => test.Key, StringComparer.Ordinal);
        DateTimeOffset now = session.Context.Time.GetUtcNow();
        List<RedGreenAmend> amends = [];
        foreach (RedGreenAmend amend in open.Amends)
        {
            amends.Add(amend.IsOpen ? amend with { RecheckedAt = now, Diff = await DiffSinceRedAsync(open, amend, cancellationToken) } : amend);
        }

        RedGreenCycle rechecked = open with
        {
            Tests = [.. open.Tests.Select(test => replaced.GetValueOrDefault(test.Key) ?? test)],
            Amends = amends,
            TestFiles = files.Rehash(open.TestFiles),
        };
        session.Save(record with { Cycles = [.. record.Cycles.Select(cycle => cycle.Number == open.Number ? rechecked : cycle)] });
        string diffs = string.Join("; ", amends.Where(amend => amend.RecheckedAt == now).Select(amend =>
            $"{TraceMatrix.ShortName(amend.MethodFilter)}: {(amend.Diff is AmendDiff diff ? $"{diff.Summary} (sha256 {diff.Sha256})" : (open.Snapshot is null ? "no diff (no red-time snapshot)" : "no diff (git could not compare it with the snapshot)"))}"));
        await session.RecordAsync(LedgerSignatures.RedAmendRechecked, $"{session.Feature.Id}: cycle {open.Number}, {classified.Count} amended tests", diffs, cancellationToken);
        await session.Output.WriteLineAsync(
            $"Red accepted for the amended tests: cycle {open.Number} stays red and its test files are locked again until '{EngineCommand.Linked} redgreen green' passes.");
        return ExitCode.Pass;
    }

    /// <summary>The tests added since the baseline and earlier cycles; null — after saying why — when the suite lost or weakened tests, or nothing is new.</summary>
    private async Task<TestCase[]?> NewTestsAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        if (await SuiteProblemAsync(record, inventory, cancellationToken))
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

    /// <summary>Whether the suite lost or weakened tests since the baseline and earlier cycles; says why when it did.</summary>
    private async Task<bool> SuiteProblemAsync(RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        HashSet<string> current = [.. inventory.Select(test => test.Key)];
        string[] deleted = [.. record.KnownTests.Where(key => !current.Contains(key)).Order(StringComparer.Ordinal)];
        if (deleted.Length > 0)
        {
            await session.FailAsync($"tests disappeared since the baseline: {string.Join(", ", deleted.Take(RedGreenSession.MaxTestsListed))}; restore them (deleting tests is not part of a red)", cancellationToken);
            return true;
        }

        if (SuiteChecks.WeakenedKnownTests(record, inventory) is { Count: > 0 } weakened)
        {
            await session.FailAsync($"{string.Join("; ", weakened)}; restore them (a red adds tests, it does not switch existing ones off)", cancellationToken);
            return true;
        }

        return false;
    }

    /// <summary>What the amend's files changed since the red-time snapshot; null without a snapshot, or when git cannot tell.</summary>
    private async Task<AmendDiff?> DiffSinceRedAsync(RedGreenCycle open, RedGreenAmend amend, CancellationToken cancellationToken) =>
        open.Snapshot is string snapshot
            && await RedSnapshots.DiffAsync(session.Manifest.Root, snapshot, amend.Files, TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds), cancellationToken) is string diff
            ? AmendDiff.From(diff, amend.Files)
            : null;

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
            await ClassifyAsync(session.Workspace, session.Manifest.Root, inventory, fresh, results, deadline, cancellationToken);
        foreach (RedGreenTest test in classified)
        {
            string requirements = test.Requirements.Count > 0 ? string.Join(",", test.Requirements) : "no Requirement trait";
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await session.Output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} ({requirements}){mark}");
            await WriteWhyAsync(verdicts[test.Key]);
        }

        await WarnAssertionFreeAsync(classified);

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

    private async Task WarnAssertionFreeAsync(List<RedGreenTest> classified)
    {
        string[] assertionFree = [.. classified.Where(test => test.AssertionFree).Select(test => TraceMatrix.ShortName(test.MethodFilter))];
        if (assertionFree.Length > 0)
        {
            await session.Output.WriteLineAsync(
                $"Warning: {assertionFree.Length} new tests assert nothing ({RedGreenTest.AssertionFreeMark}): {string.Join(", ", assertionFree.Take(RedGreenSession.MaxTestsListed))}. " +
                "Such a test passes as soon as the code returns without throwing. Add an assertion on the result; " +
                "a test that verifies by letting the production code throw may keep this warning, which trace and review show.");
        }
    }

    /// <summary>Why the test got its class, under its line; with <c>--verbose</c> also the exception, the deciding frame and the production call.</summary>
    private async Task WriteWhyAsync(RedVerdict verdict)
    {
        await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Why}");
        if (verbose)
        {
            await session.Output.WriteLineAsync($"{WhyIndent}{verdict.Detail}");
        }
    }

    /// <summary>
    /// Classifies each new test by how it failed, with why; tests that passed every time they ran are re-run together to tell flaky
    /// from green.
    /// </summary>
    private async Task<(List<RedGreenTest> Classified, FlakyCheckResult FlakyCheck, IReadOnlyDictionary<string, RedVerdict> Verdicts)> ClassifyAsync(
        TestWorkspace workspace,
        string root,
        IReadOnlyList<TestCase> inventory,
        TestCase[] fresh,
        IReadOnlyList<ExecutedTest> results,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> testClasses = inventory.Select(test => test.Class).ToHashSet(StringComparer.Ordinal);
        string[] testDirectories = [.. workspace.Apps.Select(app => app.ProjectDirectory)];
        ProductionCode production = ProductionProjects.Find(root, workspace.Apps);
        NewTestScan scan = Scan(workspace, fresh, production);
        List<(TestCase Test, RedVerdict Verdict)> first = [.. fresh.Select(test => (test, RedClassifier.Judge(
            [.. results.Where(result => result.Key == test.Key)], testClasses, testDirectories, production, scan.ProductionCalls.GetValueOrDefault(test.Key))))];

        // Only a test whose rows all passed is re-run: a theory with mixed rows is GREEN_BEFORE, not flaky.
        TestCase[] candidates = [.. first.Where(entry => entry.Verdict.Class == RedClass.GreenBefore && SuiteChecks.PassedEveryRow(results, entry.Test.Key)).Select(entry => entry.Test)];
        FlakyCheckResult flakyCheck = candidates.Length == 0
            ? new FlakyCheckResult(new HashSet<string>(StringComparer.Ordinal), 0, BudgetExhausted: false)
            : await new FlakyCheck(workspace.RunAsync, session.Context.Time).RunAsync(candidates, deadline, cancellationToken);

        List<RedGreenTest> classified = [.. first.Select(entry => new RedGreenTest(
            entry.Test.Key,
            entry.Test.MethodFilter,
            flakyCheck.Flaky.Contains(entry.Test.Key) ? RedClass.Flaky : entry.Verdict.Class,
            entry.Test.Requirements)
        {
            AssertionFree = scan.AssertionFree.Contains(entry.Test.Key),
            Rows = results.Count(result => result.Key == entry.Test.Key),
        })];
        Dictionary<string, RedVerdict> verdicts = first.ToDictionary(
            entry => entry.Test.Key,
            entry => flakyCheck.Flaky.Contains(entry.Test.Key) ? entry.Verdict with { Class = RedClass.Flaky, Why = "passed, then failed when run again" } : entry.Verdict,
            StringComparer.Ordinal);
        return (classified, flakyCheck, verdicts);
    }

    /// <summary>Which production member each new test reaches first, and which assert nothing, read from the built test assemblies.</summary>
    private static NewTestScan Scan(TestWorkspace workspace, TestCase[] fresh, ProductionCode production)
    {
        Dictionary<string, string> calls = new(StringComparer.Ordinal);
        HashSet<string> assertionFree = new(StringComparer.Ordinal);
        foreach (TestApp app in workspace.Apps)
        {
            TestCase[] tests = [.. fresh.Where(test => test.Assembly == app.AssemblyName)];
            if (tests.Length == 0)
            {
                continue;
            }

            using TestCodeScanner scanner = new(app.AssemblyPath, production.AssemblyNames);
            foreach (TestCase test in tests)
            {
                if (scanner.ProductionCall(test.Class, test.Method) is string call)
                {
                    calls[test.Key] = call;
                }
            }

            assertionFree.UnionWith(tests.Where(test => scanner.AssertsNothing(test.Class, test.Method)).Select(test => test.Key));
        }

        return new NewTestScan(calls, assertionFree);
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
