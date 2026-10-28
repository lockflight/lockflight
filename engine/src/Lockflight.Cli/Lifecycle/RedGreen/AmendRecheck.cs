using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// <c>lockflight redgreen red</c> after an amend: the amended tests must fail in production code again (RED_STUB or RED_ASSERT),
/// with nothing lost or added in the suite. They are run on the cycle's red-time snapshot with the amended files laid over it, so an
/// implementation written since the red cannot make them pass; a cycle without a snapshot re-checks them on the current tree. Then
/// their new classification replaces the old one and their files are locked again; the rest of the cycle's evidence is kept.
/// Otherwise the amend stays open, and its files writable.
/// </summary>
internal sealed class AmendRecheck(RedGreenSession session, RedJudge judge)
{
    public async Task<ExitCode> RunAsync(
        RedGreenRecord record, RedGreenCycle open, IReadOnlyList<TestCase> inventory, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (await judge.SuiteProblemAsync(record, inventory, cancellationToken))
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
            await judge.ClassifyAsync(workspace, root, inventory, amended, results, deadline, cancellationToken);
        foreach (RedGreenTest test in classified)
        {
            string mark = test.AssertionFree ? $" {RedGreenTest.AssertionFreeMark}" : string.Empty;
            await session.Output.WriteLineAsync($"{RedClassNames.ToName(test.RedClass),-12} {test.MethodFilter} (amended){mark}");
            await judge.WriteWhyAsync(verdicts[test.Key]);
        }

        await judge.WarnAssertionFreeAsync(classified);
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

    /// <summary>What the amend's files changed since the red-time snapshot; null without a snapshot, or when git cannot tell.</summary>
    private async Task<AmendDiff?> DiffSinceRedAsync(RedGreenCycle open, RedGreenAmend amend, CancellationToken cancellationToken) =>
        open.Snapshot is string snapshot
            && await RedSnapshots.DiffAsync(session.Manifest.Root, snapshot, amend.Files, TimeSpan.FromSeconds(session.Manifest.Gates.TimeoutSeconds), cancellationToken) is string diff
            ? AmendDiff.From(diff, amend.Files)
            : null;
}
