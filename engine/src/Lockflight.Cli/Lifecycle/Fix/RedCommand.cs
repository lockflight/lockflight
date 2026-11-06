using System.Globalization;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Routing;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Lifecycle.Fix;

/// <summary>
/// <c>lockflight red [--base &lt;ref&gt;] [--message &lt;subject&gt;] [--redo &lt;n&gt;] [--dry-run] [--verbose]</c>: checks the working tree as
/// a red the way the CI judge will, and commits it with a <c>Lockflight-Red</c> trailer when it is accepted. No spec, no approval,
/// no session and no lock: the judge re-runs the red commit from scratch, so nothing this command finds is trusted. The working
/// tree is written as a commit (a private index; the person's index and branch stay as they are), then built and judged against the
/// fork point (the merge base with the target branch) in a worktree kept between runs, with the base branch's policy, by the judge's
/// own code (<see cref="RedCandidate"/>). It refuses inside a feature's lifecycle (<see cref="FeatureRedirect"/>), at a workspace
/// shell root, over the base branch's <c>ci.maxRedCommits</c>, and when the branch is already at or above the class that needs a
/// spec and none resolves. Exit 0 accepted (committed, or not committed with the line to commit it), 1 refused or rejected, 3 a tool
/// broke, 4 configuration, 5 not judgeable.
/// </summary>
internal sealed class RedCommand(CliContext context) : ICommand
{
    public const string MessageOption = "--message";
    public const string RedoOption = "--redo";
    public const string DryRunFlag = "--dry-run";
    public const string VerboseFlag = "--verbose";

    private const string Prefix = "lockflight red: ";
    private const int MaxProductionFilesListed = 10;

    private const string LongPathsAdvice =
        "On Windows a deep project can pass the 260-character path limit in the red worktree: run 'git config --global core.longpaths true', turn on Windows long paths, "
        + "or set LOCKFLIGHT_TEMP to a short folder (LOCKFLIGHT_TEMP=C:\\lf)";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        int? redo = null;
        if (options.Value(RedoOption) is string redoText)
        {
            if (!int.TryParse(redoText, NumberStyles.None, CultureInfo.InvariantCulture, out int redoLabel) || redoLabel < 1)
            {
                return await RefuseAsync(ExitCode.Config, $"{RedoOption} takes the cycle number of a red commit on this branch (such as 2), not '{TerminalText.Clean(redoText)}'");
            }

            redo = redoLabel;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        TimeSpan gitLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        if (manifest.Units.Count > 0)
        {
            return await RefuseAsync(ExitCode.Config, "this is a workspace shell; a red commit belongs to one repository: run lockflight red in the unit repository whose tests you changed");
        }

        if (await RedSnapshots.TopLevelAsync(manifest.Root, gitLimit, cancellationToken) is not string top)
        {
            return await RefuseAsync(ExitCode.Config, $"{manifest.Root} is not inside a git repository; a red is a commit, so it needs one");
        }

        top = Path.GetFullPath(top);
        if (await RedCommitter.RefusalAsync(manifest.Root, gitLimit, cancellationToken) is string notCommittable)
        {
            return await RefuseAsync(ExitCode.Fail, notCommittable);
        }

        if (FeatureRedirect.Refusal(manifest, context.Environment) is string inFeature)
        {
            return await RefuseAsync(ExitCode.Fail, inFeature);
        }

        RedBaseResolution target = await RedBase.ResolveAsync(top, manifest, options.Value(RedBase.Option), cancellationToken);
        if (target.Commit is not string baseCommit)
        {
            return await RefuseAsync(ExitCode.Config, target.Error!);
        }

        if (target.Note is string note)
        {
            await context.Output.WriteLineAsync(note);
        }

        RedSnapshot snapshot = await RedSnapshots.TakeAsync(manifest.Root, [], gitLimit, cancellationToken);
        if (snapshot.Commit is not string candidateCommit)
        {
            return await RefuseAsync(ExitCode.Error, $"the working tree could not be written as a commit ({snapshot.NotTaken})");
        }

        string? branch = GitBranch.Current(top);
        JudgeInputs inputs = new(top, candidateCommit, baseCommit, branch, JudgePlatform.Local, JudgeEvent.PullRequest,
            RedCache.DirectoryOf(manifest.Root), ShellPath: null, ReviewExpected: false);
        (JudgePlan? planned, ExitCode planFailure) = await PlanAsync(inputs, cancellationToken);
        if (planned is not JudgePlan plan)
        {
            return planFailure;
        }

        foreach (RedCommit malformed in plan.Reds.All.Where(red => red.Malformed is not null))
        {
            await context.Output.WriteLineAsync($"Warning: {CommitIds.Short(malformed.Commit)} ({malformed.Subject}) is not a red: {malformed.Malformed}. The judge reports it too.");
        }

        RedLabelChoice choice = RedLabels.Choose(plan.Reds, redo, plan.Policy.Ci.MaxRedCommits);
        if (choice.Label is not int cycle)
        {
            return await RefuseAsync(choice.Refusal, choice.Why!);
        }

        if (await ClassRefusalAsync(plan, cancellationToken) is string needsSpec)
        {
            return await RefuseAsync(ExitCode.Fail, needsSpec);
        }

        await context.Output.WriteLineAsync(
            $"lockflight red: cycle {cycle}{(redo is null ? string.Empty : " (redo)")} on {branch ?? "HEAD"}, against {target.Reference} (fork point {CommitIds.Short(plan.MergeBase!)}), "
            + $"judged as CI judges it, with {target.Reference}'s policy");
        await WriteProductionAsync(plan, cancellationToken);

        string label = cycle.ToString(CultureInfo.InvariantCulture);
        RedCommit candidate = new(candidateCommit, "working tree", string.Empty, label, Malformed: null, Superseded: false) { ForkPoint = plan.MergeBase };
        RedCommit[] earlier = [.. plan.Reds.Cycles.Where(red => red.Label != label)];
        bool dryRun = options.Flag(DryRunFlag);
        RedCache? cache = await CacheAsync(top, plan, cancellationToken);
        RedCheckResult result;
        try
        {
            result = await new RedCheck(context, plan, cache, dryRun).RunAsync(candidate, earlier, await StructuralJudge.IdsAsync(plan, cancellationToken), cancellationToken);
        }
        catch (PathTooLongException exception)
        {
            return await RefuseAsync(ExitCode.Error, $"{exception.Message}. {LongPathsAdvice}");
        }

        if (result.Cycle is not CycleJudgement judged)
        {
            return await RefuseAsync(ExitCode.Error, $"{result.Problem}; this says nothing about the tests");
        }

        if (result.Tree is TreeListing tree && judged.Tests.Count > 0)
        {
            string worktreeRoot = await RedCheck.WorktreeDirectoryAsync(top, context.Environment, cancellationToken) ?? top;
            await new RedReport(context.Output, top, gitLimit, context.Time, options.Flag(VerboseFlag)).WriteAsync(judged, tree, worktreeRoot, cancellationToken);
        }

        string summary = $"branch {branch ?? "HEAD"}: cycle {cycle}";
        return judged.Verdict switch
        {
            CheckVerdict.Pass => await AcceptAsync(manifest, judged, candidateCommit, cycle, options.Value(MessageOption), branch, dryRun, cache, cancellationToken),
            CheckVerdict.Fail => await RejectAsync(manifest, judged, summary, cancellationToken),
            CheckVerdict.Error => await RefuseAsync(ExitCode.Error, $"ERROR {judged.Why}; this says nothing about the tests{LongPathsNote(judged.Why)}"),
            _ => await NotEarnedAsync(judged),
        };
    }

    /// <summary>
    /// The judge's plan for the candidate as the head: the merge base, the base policy, the reds on the branch with their fork points,
    /// and the class routed under the base branch's policy (the workspace shell's too, for a unit). Null after saying why.
    /// </summary>
    private async Task<(JudgePlan? Plan, ExitCode Failure)> PlanAsync(JudgeInputs inputs, CancellationToken cancellationToken)
    {
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, context.Environment, cancellationToken);
        if (planning.Plan is not JudgePlan plan)
        {
            return (null, await RefuseAsync(planning.Failure, planning.Error!));
        }

        ShellPolicyReading shell = await JudgeShell.ReadAsync(plan, cancellationToken);
        if (shell.Error is string shellError)
        {
            return (null, await RefuseAsync(ExitCode.Config, shellError));
        }

        if (shell.Policy is ShellPolicy shellPolicy)
        {
            planning = await JudgePlanner.PlanAsync(inputs, context.Environment, shellPolicy, cancellationToken);
            if (planning.Plan is not JudgePlan shellPlan)
            {
                return (null, await RefuseAsync(planning.Failure, planning.Error!));
            }

            plan = shellPlan;
        }

        if (plan.Policy.Manifest is null)
        {
            await context.Output.WriteLineAsync(
                $"Note: the target branch ({CommitIds.Short(plan.Policy.Commit)}) has no {Manifest.FileName}, so this red is judged with the default policy, and the CI judge will refuse to judge the pull request until it has one.");
        }

        return (plan with { Shell = shell.Policy }, ExitCode.Pass);
    }

    /// <summary>
    /// The class of everything since the fork point, the working tree included, under the base branch's policy. At or above the
    /// class that needs a spec (<c>routing.require.specFrom</c>) with no spec for the branch, the pull request can only end
    /// NOT-EARNED: refused, with the class and its strongest signals.
    /// </summary>
    private static async Task<string?> ClassRefusalAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        if (plan.Requirements.LevelOf(RequirementKind.SpecAndTrace) != RequirementLevel.Required)
        {
            return null;
        }

        SpecResolution spec = plan.Shell is ShellPolicy shell
            ? await ShellSpecSource.ResolveAsync(plan, shell, plan.Inputs.Branch, [], cancellationToken)
            : await TreeSpecSource.ResolveAsync(plan, plan.Inputs.Branch, [], cancellationToken);
        if (spec.SpecText is not null)
        {
            return null;
        }

        Classification classification = plan.Class.Classification;
        string signals = string.Join("; ", classification.Signals.Where(signal => signal.RaisesTo == classification.Class).Take(3)
            .Select(signal => $"{signal.Id}{(signal.Where.Length == 0 ? string.Empty : $" {signal.Where}")}"));
        return $"this branch is class {ChangeClassNames.ToName(classification.Class)} since the fork point{(signals.Length == 0 ? string.Empty : $" ({signals})")}, "
            + "and the base branch's policy needs a spec from that class; with no spec the pull request can only end NOT-EARNED. "
            + "This change needs a spec: use the feature flow (/lockflight:feature, or write the spec and have it approved), not lockflight red";
    }

    /// <summary>Names the production files the red holds, or says there are none: a fix's strongest evidence is a red on unchanged code.</summary>
    private async Task WriteProductionAsync(JudgePlan plan, CancellationToken cancellationToken)
    {
        if (!plan.Class.ProductionChanged)
        {
            await context.Output.WriteLineAsync("Production unchanged since the fork point: the new tests fail on the target branch's code.");
            return;
        }

        string preset = RoutingPresetNames.ToName((plan.Policy.Manifest?.Routing ?? RoutingSettings.Default).Preset);
        RoutingPolicy routing = RoutingPolicy.From(plan.Policy.Manifest, new PolicySource(plan.Inputs.Base!, plan.Policy.Commit, plan.Policy.ManifestBlob, preset));
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(plan.Inputs.Root, plan.MergeBase!, plan.Inputs.Head, cancellationToken);
        string[] files = reading.ChangeSet is ChangeSet changeSet
            ? [.. ChangeAnalyzer.Analyze(changeSet, routing.ExcludedGlobs).Files
                .Where(file => file.Role is FileRole.Production or FileRole.Markup)
                .Select(file => $"{file.Path} (+{file.Added.Count} −{file.Removed.Count})")]
            : [];
        string listed = files.Length == 0 ? "see git diff against the fork point" : string.Join(", ", files.Take(MaxProductionFilesListed))
            + (files.Length > MaxProductionFilesListed ? $" and {files.Length - MaxProductionFilesListed} more" : string.Empty);
        await context.Output.WriteLineAsync($"Production files in this red (they go into the red commit): {listed}");
    }

    private async Task<RedCache?> CacheAsync(string top, JudgePlan plan, CancellationToken cancellationToken)
    {
        if (RedCache.EngineHash(context.Engine) is not string engineHash)
        {
            return null;
        }

        ProcessResult sdk = await ProcessRunner.RunAsync("dotnet", ["--version"], top, cancellationToken);
        return sdk.ExitCode == 0 && sdk.Output.Trim() is { Length: > 0 } version
            ? new RedCache(RedCache.DirectoryOf(top), plan.Policy.Commit, engineHash, version)
            : null;
    }

    private async Task<ExitCode> AcceptAsync(
        Manifest manifest, CycleJudgement judged, string snapshot, int cycle, string? message, string? branch, bool dryRun, RedCache? cache, CancellationToken cancellationToken)
    {
        string trailer = $"{RedCommitter.Trailer}: {cycle}";
        if (dryRun)
        {
            await context.Output.WriteLineAsync($"Red accepted (dry run): {judged.Why}. Nothing was committed; without {DryRunFlag} the red commit would carry '{trailer}'.");
            return ExitCode.Pass;
        }

        string subject = message is { Length: > 0 } given ? given : $"test: red for {branch ?? "HEAD"}, cycle {cycle}";
        TimeSpan gitLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        RedCommitOutcome outcome = await RedCommitter.CommitAsync(manifest.Root, snapshot, subject, cycle, gitLimit, cancellationToken);
        if (outcome.Commit is not string committed)
        {
            await context.Output.WriteLineAsync($"Red accepted: {judged.Why}. {outcome.Message}");
            await RecordAsync(manifest, LedgerSignatures.RedAccepted, $"branch {branch ?? "HEAD"}: cycle {cycle}, {judged.Tests.Count} tests", cancellationToken);
            return ExitCode.Pass;
        }

        if (outcome.TreeChanged)
        {
            bool undone = await UndoAsync(manifest.Root, committed, gitLimit, cancellationToken);
            return await RefuseAsync(ExitCode.Fail,
                $"a commit hook changed the red's tree, so the commit is not the red this run judged. {(undone ? "The commit was undone (git reset --soft HEAD~1); the hook's changes are staged" : $"HEAD moved, so the commit {CommitIds.Short(committed)} was left as it is: check it before you push")}. "
                + "Run lockflight red again on the hook's result");
        }

        cache?.WriteRed(committed, new CachedRed(Accepted: true, judged.Tests));
        await RecordAsync(manifest, LedgerSignatures.RedAccepted, $"branch {branch ?? "HEAD"}: cycle {cycle}, {judged.Tests.Count} tests", cancellationToken);
        await context.Output.WriteLineAsync($"Red accepted: {judged.Why}. {outcome.Message}");
        await context.Output.WriteLineAsync(
            $"Next: fix the code until these tests pass, then commit; CI re-runs this red at {CommitIds.Short(committed)}. "
            + $"To redo it before you push: git reset --soft HEAD~1. After the push: lockflight red {RedoOption} {cycle}.");
        return ExitCode.Pass;
    }

    private async Task<ExitCode> RejectAsync(Manifest manifest, CycleJudgement judged, string summary, CancellationToken cancellationToken)
    {
        if (judged.Reason == ReasonCode.CompileRed)
        {
            await context.Output.WriteLineAsync(judged.Why);
            await RecordAsync(manifest, LedgerSignatures.RedCompileError, summary, cancellationToken);
            return ExitCode.Fail;
        }

        RedGreenTest[] rejected = [.. judged.Tests.Where(test => !RedClassNames.IsAccepted(test.RedClass))];
        if (rejected.Length == 0)
        {
            // The suite lost or weakened a known test, or nothing is new: the judge's own words.
            return await RefuseAsync(ExitCode.Fail, judged.Why);
        }

        await context.Output.WriteLineAsync($"Red rejected: {rejected.Length} of {judged.Tests.Count} new tests do not fail for the right reason. A red must be RED_STUB or RED_ASSERT. Nothing was committed.");
        foreach (RedClass redClass in rejected.Select(test => test.RedClass).Distinct())
        {
            await context.Output.WriteLineAsync($"  {RedClassNames.ToName(redClass)}: {RedClassFixes.For(redClass)}.");
        }

        await RecordAsync(manifest, LedgerSignatures.RedRejected, $"{summary}: {string.Join(",", rejected.Select(test => RedClassNames.ToName(test.RedClass)).Distinct())}", cancellationToken);
        return ExitCode.Fail;
    }

    private async Task<ExitCode> NotEarnedAsync(CycleJudgement judged)
    {
        await context.Output.WriteLineAsync($"NOT-EARNED the red cannot be judged: {judged.Why}{LongPathsNote(judged.Why)}");
        return ExitCode.NotEarned;
    }

    /// <summary>Undoes the engine's own commit when HEAD is still that commit; false when HEAD moved or git refused.</summary>
    private static async Task<bool> UndoAsync(string root, string committed, TimeSpan gitLimit, CancellationToken cancellationToken) =>
        await SnapshotGit.OutputAsync(["rev-parse", "HEAD"], root, gitLimit, cancellationToken) == committed
        && (await SnapshotGit.RunAsync(["reset", "--soft", "-q", "HEAD~1"], root, gitLimit, cancellationToken)).ExitCode == 0;

    private async Task RecordAsync(Manifest manifest, string signature, string summary, CancellationToken cancellationToken) =>
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(new LedgerEvent(LedgerEventType.EvidenceRecorded, signature, summary, Detail: null), cancellationToken);

    private async Task<ExitCode> RefuseAsync(ExitCode exitCode, string message)
    {
        await context.Error.WriteLineAsync(Prefix + message);
        return exitCode;
    }

    private static string LongPathsNote(string why) =>
        why.Contains("Filename too long", StringComparison.OrdinalIgnoreCase) || why.Contains("PathTooLong", StringComparison.Ordinal) ? $". {LongPathsAdvice}" : string.Empty;
}
