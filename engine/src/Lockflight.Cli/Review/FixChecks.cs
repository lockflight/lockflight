using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// Checks a confirmed finding against the change's own tests, using the reviewer's own fix. The fix is applied to copies of the
/// production files (<see cref="FixPatch"/>), built with the reproduction into the review's fix folder
/// (<see cref="ReviewBuildFolder.FixName"/>), and the reproduction runs with the change's own tests in its test project
/// (<see cref="ChangeTests"/>). When the fix makes the reproduction pass and breaks one of those tests, the reviewer's expectation
/// contradicts what the author's tests encode: the finding is <see cref="FindingOutcome.Contested"/> and names the test. A fix that
/// does not apply or build goes back to the reviewer once, within the run's budget; if it still does not, the finding is contested
/// too. A fix that holds leaves the finding confirmed and fix-checked. Anything else (the reproduction still fails, the run did not
/// report) leaves it confirmed and unchecked, as without a fix. This is a guardrail on the reviewer's evidence, not a proof: the
/// fix and the reproduction are both the reviewer's, and the author's tests can be wrong too. At most one check (and one repair)
/// per finding, each build and run within the gates' time limit.
/// </summary>
internal static class FixChecks
{
    private const int MaxUsingRepairs = 3;

    /// <param name="checkable">The ids of findings that may be fix-checked: new findings, not ones open from earlier runs.</param>
    public static async Task<IReadOnlyList<ReviewFinding>> CheckAsync(
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<TestApp> apps,
        IReadOnlyList<TestCase> ownTests,
        string root,
        TimeSpan timeLimit,
        ReproductionRepairer? repairer,
        IReadOnlySet<string> checkable,
        CancellationToken cancellationToken)
    {
        ReviewFinding[] checkedFindings = [.. findings];
        int[] candidates = [.. findings.Select((finding, index) => (finding, index))
            .Where(item => IsCheckable(item.finding, checkable))
            .Select(item => item.index)];
        if (candidates.Length == 0 || apps.Count == 0)
        {
            return checkedFindings;
        }

        using ReviewBuildFolder folder = ReviewBuildFolder.Acquire(root);
        foreach (int index in candidates)
        {
            ReviewFinding finding = checkedFindings[index];
            TestApp app = NearestTestProject.Choose(apps, root, finding.File);
            string[] own = [.. ownTests.Where(test => test.Assembly == app.AssemblyName).Select(test => test.MethodFilter)];
            ReproductionCandidate candidate = new(index, Reproductions.MethodName(finding.Reproduction!)!, finding.Reproduction!);
            FixCheckVerdict verdict = await CheckOnceAsync(app, candidate, finding.Fix!, own, root, folder.FixName, timeLimit, cancellationToken);
            int attempts = 1;
            if (verdict.Result is FixCheckResult.DidNotApply or FixCheckResult.DidNotBuild && repairer is not null)
            {
                FixRepair repair = await repairer.RepairFixAsync(new FixRepairRequest(finding, verdict.Result, verdict.Errors ?? string.Empty), cancellationToken);
                if (repair.Asked)
                {
                    attempts = 2;
                }

                if (repair.Fix is string repaired)
                {
                    finding = finding with { Fix = repaired };
                    verdict = await CheckOnceAsync(app, candidate, repaired, own, root, folder.FixName, timeLimit, cancellationToken);
                }
            }

            checkedFindings[index] = Apply(finding, verdict) with { FixAttempts = attempts };
        }

        return checkedFindings;
    }

    /// <summary>Whether the finding gets a fix check: new, confirmed in this run, with a runnable reproduction and a fix.</summary>
    public static bool IsCheckable(ReviewFinding finding, IReadOnlySet<string> checkable) =>
        finding is { Outcome: FindingOutcome.Confirmed, Carried: false, Fix: not null }
        && Reproductions.IsRunnable(finding)
        && checkable.Contains(finding.Id);

    /// <summary>The verdict of a fixed build that ran: from the reproduction's result and those of the change's own tests (runner keys).</summary>
    public static FixCheckVerdict Judge(IReadOnlyList<ExecutedTest> results, string reproductionKey, IReadOnlyList<string> ownKeys)
    {
        ExecutedTest[] reproduction = [.. results.Where(result => result.Key == reproductionKey)];
        ILookup<string, ExecutedTest> own = results.Where(result => ownKeys.Contains(result.Key)).ToLookup(result => result.Key, StringComparer.Ordinal);
        int ran = own.Count;
        if (reproduction.Length == 0 || reproduction.Any(row => row.Outcome is not (TestOutcome.Pass or TestOutcome.Fail)))
        {
            return new FixCheckVerdict(FixCheckResult.NotRun, [], null) { OwnTestsChecked = ran };
        }

        if (reproduction.Any(row => row.Outcome == TestOutcome.Fail))
        {
            return new FixCheckVerdict(FixCheckResult.DidNotFix, [], null) { OwnTestsChecked = ran };
        }

        string[] broken = [.. own.Where(rows => rows.Any(row => row.Outcome == TestOutcome.Fail)).Select(rows => MethodOf(rows.Key)).Order(StringComparer.Ordinal)];
        return broken.Length > 0
            ? new FixCheckVerdict(FixCheckResult.BreaksOwnTest, broken, null) { OwnTestsChecked = ran }
            : new FixCheckVerdict(FixCheckResult.Held, [], null) { OwnTestsChecked = ran };
    }

    /// <summary>The finding with the check's result: contested when the fix breaks an own test or does not apply or build, else as it was.</summary>
    public static ReviewFinding Apply(ReviewFinding finding, FixCheckVerdict verdict) => finding with
    {
        Outcome = verdict.Result is FixCheckResult.BreaksOwnTest or FixCheckResult.DidNotApply or FixCheckResult.DidNotBuild ? FindingOutcome.Contested : finding.Outcome,
        FixCheck = verdict.Result,
        ConflictingTests = verdict.ConflictingTests,
        FixCheckErrors = verdict.Errors,
        OwnTestsChecked = verdict.OwnTestsChecked,
    };

    private static async Task<FixCheckVerdict> CheckOnceAsync(
        TestApp app, ReproductionCandidate candidate, string fix, string[] own, string root, string outputFolder, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        FixPatchResult patch = FixPatch.Prepare(fix, root);
        if (patch.Error is string error)
        {
            return new FixCheckVerdict(FixCheckResult.DidNotApply, [], error);
        }

        List<string> usings = [.. TestProjectProfiles.Usings(app.ProjectDirectory)];
        for (int usingRepairs = 0; ; usingRepairs++)
        {
            ReproductionBuildResult build = await ReproductionBuild.RunAsync(app, [candidate], usings, patch.Files, own, root, outputFolder, timeLimit, cancellationToken);
            switch (build.Status)
            {
                case ReproductionBuildStatus.Ran:
                    return Judge(build.Results, ReproductionBuild.Key(app, candidate), [.. own.Select(filter => $"{app.AssemblyName}:{filter}")]);
                case ReproductionBuildStatus.NotRun:
                    return new FixCheckVerdict(FixCheckResult.NotRun, [], null);
            }

            // A using directive the generated file cannot resolve is dropped, as for the reproductions themselves.
            int[] unresolved = [.. build.Errors.Select(compileError => ReproductionBuild.UsingAt(compileError, usings.Count)).OfType<int>().Distinct().OrderDescending()];
            if (unresolved.Length == 0 || usingRepairs >= MaxUsingRepairs)
            {
                return new FixCheckVerdict(FixCheckResult.DidNotBuild, [], CompileErrors.Summary(build.Errors));
            }

            foreach (int index in unresolved)
            {
                usings.RemoveAt(index);
            }
        }
    }

    /// <summary><c>Assembly:Namespace.Class.Method</c> → <c>Namespace.Class.Method</c>.</summary>
    private static string MethodOf(string key) => key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
}
