using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// <c>lockflight review [--feature id] [--base ref] [--model name] [--budget usd]</c> — the review: the engine builds the prompt from
/// the spec and the diff, runs a read-only <c>claude -p</c> with a JSON schema, confirms blocker/major findings by running their
/// reproductions (and re-runs those of findings still open from earlier runs; a new finding's reproduction that does not compile is
/// sent back to the reviewer once, within the run's budget), and stores the run. A run counts as the review of record only when
/// the reviewer was shown the whole diff (at most <c>review.maxDiffCharacters</c>; generated files are listed, not shown), with the manifest's model, budget and diff limit, against a base whose commit is the merge base of
/// HEAD and the target branch (<c>review.target</c>, else <c>origin/HEAD</c>'s branch, else <c>main</c>; also the default base); others are recorded and say why not.
/// A new confirmed finding with a fix is checked against the change's own tests (<see cref="FixChecks"/>) and contested when its fix
/// contradicts them. Exit 1 when an open blocker/major finding is confirmed, 5 (NOT-EARNED) when one is unverified or contested or
/// the run is not of record, 0 otherwise. Advisory (guardrail): it runs with the developer's credentials and the agent can skip it.
/// </summary>
internal sealed class ReviewCommand(CliContext context)
{
    private const int MaxGuidanceCharacters = 8_000;
    private const string GuidanceFile = "CLAUDE.md";
    private const string DefaultTarget = "main";

    private static readonly TimeSpan VersionTimeLimit = TimeSpan.FromSeconds(30);
    private static readonly string[] ValueOptions = ["--feature", "--base", "--model", "--budget"];

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("review", args, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options
            || await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight: {lookup.Error}");
            return ExitCode.Config;
        }

        if (!File.Exists(feature.SpecPath))
        {
            await context.Error.WriteLineAsync($"lockflight: {Path.GetRelativePath(manifest.Root, feature.SpecPath)} does not exist; write the spec first");
            return ExitCode.Config;
        }

        if (RedGreenRecord.Load(RedGreenRecord.PathFor(feature)) is RedGreenRecord record && TestFileHashes.Refusal(manifest.Root, record) is string changed)
        {
            await context.Error.WriteLineAsync($"lockflight: no review: {changed}");
            return ExitCode.Fail;
        }

        decimal budget = manifest.Review.BudgetUsd;
        if (options.Value("--budget") is string budgetText && !TryParseBudget(budgetText, out budget))
        {
            await context.Error.WriteLineAsync($"lockflight review: --budget takes an amount in US dollars above 0 and at most {ReviewSettings.MaxBudgetUsd}, such as 3.00 (got '{budgetText}')");
            return ExitCode.Config;
        }

        if (Executables.FindOnPath("claude", context.Environment.Get(EnvironmentVariables.Path)) is not string claude)
        {
            await context.Output.WriteLineAsync("NOT-EARNED claude is not on PATH; the review of record needs Claude Code signed in (subscription or API key)");
            return ExitCode.NotEarned;
        }

        string target = manifest.Review.Target ?? await Git.DefaultBranchAsync(manifest.Root, DefaultTarget, cancellationToken);
        string baseRef = options.Value("--base") ?? target;
        if (await CodeDiffAsync(manifest.Root, baseRef, cancellationToken) is not string diff)
        {
            await context.Error.WriteLineAsync($"lockflight: git diff against '{baseRef}' failed; pass --base <ref> (in a workspace, every unit repository needs that ref)");
            return ExitCode.Config;
        }

        if (diff.Trim().Length == 0)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED no changes against {baseRef}; nothing to review");
            return ExitCode.NotEarned;
        }

        ReviewBase reviewBase = new(
            baseRef, await Git.CommitOfAsync(manifest.Root, baseRef, cancellationToken), target, await Git.MergeBaseAsync(manifest.Root, target, cancellationToken));
        ReviewedTree tree = await ReviewedTrees.FindAsync(manifest.Root, feature.SpecPath, cancellationToken);
        return await ReviewAsync(
            manifest, feature, claude, reviewBase, ReviewDiff.Fit(diff, manifest.Review.MaxDiffCharacters, manifest.Root), tree, options.Value("--model") ?? manifest.Review.Model, budget, cancellationToken);
    }

    /// <summary>Asks the reviewer, confirms its findings and the open ones, stores the run and turns the outcome into the exit code.</summary>
    private async Task<ExitCode> ReviewAsync(
        Manifest manifest,
        Feature feature,
        string claude,
        ReviewBase reviewBase,
        ReviewDiff diff,
        ReviewedTree tree,
        string model,
        decimal budget,
        CancellationToken cancellationToken)
    {
        DateTimeOffset started = context.Time.GetUtcNow();
        await context.Output.WriteLineAsync($"Reviewing {feature.Id} against {reviewBase.Ref} with {model} (budget ${ReviewOfRecord.Usd(budget)})…");
        if (!diff.IsComplete)
        {
            // Said before the reviewer runs, so a person can stop a run that cannot count before it spends the budget.
            await context.Output.WriteLineAsync($"Warning: this run will not be the review of record: {ReviewOfRecord.Incomplete(diff)}.");
        }

        if (!tree.IsClean)
        {
            await context.Output.WriteLineAsync($"Warning: this run will not be the review of record: {ReviewOfRecord.NotCommitted(tree)}.");
        }

        if (diff.Generated.Count > 0)
        {
            await context.Output.WriteLineAsync(
                $"Generated files {GeneratedFiles.Status} (the reviewer gets their path, size and hash): {string.Join(", ", diff.Generated.Select(file => file.Path))}.");
        }

        string requirements = await File.ReadAllTextAsync(feature.SpecPath, cancellationToken);
        string? guidanceCommit = reviewBase.MergeBase ?? reviewBase.Commit;
        string prompt = ReviewPrompt.Build(new ReviewPromptData(
            requirements,
            Path.GetRelativePath(manifest.Root, feature.SpecPath).Replace('\\', '/'),
            diff.Shown,
            await BaseGuidanceAsync(manifest.Root, guidanceCommit, cancellationToken),
            $"{GuidanceFile} at the base commit {ReviewOfRecord.Short(guidanceCommit)}",
            ReproductionContext.Build(manifest.Root, diff.Whole),
            ReviewPrompt.NewMarker()));
        string? claudeVersion = await ClaudeVersionAsync(manifest, claude, cancellationToken);
        ProcessResult reply = await AskReviewerAsync(manifest, claude, model, ReviewOfRecord.Usd(budget), prompt, cancellationToken);
        ReviewOutcome outcome = ReviewResultParser.Parse(reply.Output);
        if (outcome.Error is not null)
        {
            // A verdict line like the gates' ERROR: the review did not happen, which says nothing about the code.
            await context.Output.WriteLineAsync($"ERROR {outcome.Error}");
            return ExitCode.Error;
        }

        ReviewHistory history = ReviewEvidence.Load(feature.RecordDirectory);
        foreach (string problem in history.Unreadable)
        {
            await context.Error.WriteLineAsync($"lockflight: warning: {problem}; restore it from version control or delete it");
        }

        // Repairs of reproductions share the run's budget with the review itself.
        ReproductionRepairer repairer = new(
            async (repairPrompt, repairBudget, repairCancellation) => ReviewResultParser.Parse(
                (await AskReviewerAsync(manifest, claude, model, ReviewOfRecord.Usd(repairBudget), repairPrompt, repairCancellation)).Output),
            budget - outcome.CostUsd);
        HashSet<string> repairable = [.. outcome.Findings.Select(finding => finding.Id).Where(id => !history.Open.Any(tracked => tracked.Finding.Id == id))];
        IReadOnlyList<ReviewFinding> anchored = [.. FindingLifecycle.Carry(outcome.Findings, history).Select(finding => Anchor(finding, requirements))];
        (IReadOnlyList<ReviewFinding> judged, FixCheckSpend fixChecks) = await ConfirmFindingsAsync(manifest, feature, diff.Whole, anchored, repairer, repairable, cancellationToken);
        outcome = outcome with
        {
            Findings = FindingLifecycle.Resolve(judged, history),
            CostUsd = outcome.CostUsd + repairer.CostUsd,
            DurationMs = outcome.DurationMs + repairer.DurationMs,
            Usage = new ReviewUsage(
                outcome.Usage.InputTokens + repairer.Usage.InputTokens,
                outcome.Usage.OutputTokens + repairer.Usage.OutputTokens,
                outcome.Usage.CacheReadInputTokens + repairer.Usage.CacheReadInputTokens,
                outcome.Usage.CacheCreationInputTokens + repairer.Usage.CacheCreationInputTokens),
            Repairs = new RepairSpend(repairer.Calls, repairer.CostUsd),
            FixChecks = fixChecks,
        };
        ReviewRun run = new(
            ReviewEvidence.NewRunId(feature.RecordDirectory, started),
            started,
            feature.Id,
            reviewBase,
            await Git.HeadAsync(manifest.Root, cancellationToken) ?? Git.UnknownCommit,
            Sha256(diff.Whole),
            Sha256(prompt),
            model,
            budget,
            manifest.Review,
            claudeVersion,
            (long)(context.Time.GetUtcNow() - started).TotalMilliseconds,
            ReviewOfRecord.Reasons(manifest.Review, model, budget, reviewBase, diff, tree))
        {
            Generated = diff.Generated,
        };
        string evidence = ReviewEvidence.Save(feature.RecordDirectory, run, outcome);
        await PrintAsync(run, outcome, manifest.Root, evidence);
        return await RecordAsync(manifest, run, outcome, cancellationToken);
    }

    /// <summary>The first line of <c>claude --version</c>; null when it fails.</summary>
    private async Task<string?> ClaudeVersionAsync(Manifest manifest, string claude, CancellationToken cancellationToken)
    {
        ProcessResult version = await context.Claude(claude, ["--version"], manifest.Root, VersionTimeLimit, null, cancellationToken);
        return version.ExitCode == 0 && version.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is [string first, ..] ? first : null;
    }

    /// <summary>
    /// Runs a read-only <c>claude -p</c> with the review schema; the prompt goes through stdin. Every mode is passed explicitly, so
    /// a change of Claude Code's defaults for <c>-p</c> does not change the reviewer: <c>--restricted</c> ignores user, project and
    /// local settings (no hooks, no plugins), <c>--safe-mode</c> leaves out the working tree's <c>CLAUDE.md</c>, skills, agents and
    /// other customizations (the change under review could have rewritten them), <c>--strict-mcp-config</c> skips MCP servers,
    /// <c>--tools</c> leaves only file reads, and <c>--permission-mode dontAsk</c> denies anything that would ask. Bare mode (<c>--bare</c>) is chosen only when an API key
    /// is set, because it never reads a subscription sign-in.
    /// </summary>
    private async Task<ProcessResult> AskReviewerAsync(
        Manifest manifest, string claude, string model, string budget, string prompt, CancellationToken cancellationToken)
    {
        // Not an argument: a diff easily exceeds Windows' 32 K command line and Linux's 128 KiB per-argument limit.
        string promptFile = Path.Combine(Path.GetTempPath(), $"lockflight-review-{Guid.CreateVersion7()}.md");
        string[] signIn = string.IsNullOrWhiteSpace(context.Environment.Get(EnvironmentVariables.AnthropicApiKey)) ? [] : ["--bare"];
        try
        {
            await File.WriteAllTextAsync(promptFile, prompt, cancellationToken);
            return await context.Claude(
                claude,
                [.. signIn, "-p", "--restricted", "--safe-mode", "--strict-mcp-config", "--tools", "Read,Grep,Glob", "--permission-mode", "dontAsk",
                 "--model", model, "--json-schema", ReviewPrompt.Schema, "--output-format", "json", "--no-session-persistence", "--max-budget-usd", budget],
                manifest.Root,
                TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds),
                promptFile,
                cancellationToken);
        }
        finally
        {
            File.Delete(promptFile);
        }
    }

    /// <summary>
    /// The project's <c>CLAUDE.md</c> as it is at the base commit (bounded), never the working tree's: the change under review
    /// cannot rewrite the guidance its reviewer reads. Null when the base has none or git cannot read it.
    /// </summary>
    private static async Task<string?> BaseGuidanceAsync(string root, string? commit, CancellationToken cancellationToken)
    {
        if (commit is null)
        {
            return null;
        }

        ProcessResult show = await ProcessRunner.RunAsync("git", ["show", $"{commit}:{GuidanceFile}"], root, cancellationToken);
        if (show.ExitCode != 0 || show.Output.Trim().Length == 0)
        {
            return null;
        }

        return show.Output.Length > MaxGuidanceCharacters ? show.Output[..MaxGuidanceCharacters] + "\n[guidance truncated]" : show.Output;
    }

    /// <summary>
    /// The code change since the base: the project's own diff without Lockflight's records and the specs, then — in a workspace —
    /// each unit repository's diff with its unit path in the file names. Null when git fails in any of them.
    /// </summary>
    private static async Task<string?> CodeDiffAsync(string root, string baseRef, CancellationToken cancellationToken)
    {
        List<string> diffs = [];
        foreach (RepoRoot repo in WorkspaceRepos.Of(root))
        {
            string[] scope = repo.Prefix.Length == 0 ? [".", ":(exclude).lockflight", $":(exclude){FeatureLocator.SpecsDirectory}"] : [];
            ProcessResult diff = await ProcessRunner.RunAsync(
                "git", ["diff", "--no-color", "--no-ext-diff", $"--src-prefix=a/{repo.Prefix}", $"--dst-prefix=b/{repo.Prefix}", baseRef, "--", .. scope], repo.Directory, cancellationToken);
            if (diff.ExitCode != 0)
            {
                return null;
            }

            diffs.Add(diff.Output);
        }

        return string.Concat(diffs);
    }

    /// <summary>
    /// Runs the reproductions of blocker/major findings when there are any and the tests build and list; otherwise blocker/major
    /// findings stay unverified, because a finding nobody could check is not a pass. Then checks the fix of each new confirmed
    /// finding that has one against the change's own tests (<see cref="FixChecks"/>), and says how many checks ran and how long they took.
    /// </summary>
    private async Task<(IReadOnlyList<ReviewFinding> Findings, FixCheckSpend FixChecks)> ConfirmFindingsAsync(
        Manifest manifest,
        Feature feature,
        string diff,
        IReadOnlyList<ReviewFinding> findings,
        ReproductionRepairer repairer,
        IReadOnlySet<string> repairable,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewFinding> checks = FindingLifecycle.WithEarlierReproductions(findings);
        if (!checks.Any(Reproductions.IsRunnable))
        {
            return (FindingLifecycle.CombineReproductions(findings, [.. checks.Select(Reproductions.Unjudged)]), FixCheckSpend.None);
        }

        TimeSpan timeLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        TestWorkspace workspace = TestWorkspaces.For(manifest, feature);
        if (await workspace.BuildAsync(cancellationToken) is not null)
        {
            return (FindingLifecycle.CombineReproductions(findings, [.. checks.Select(Reproductions.Unjudged)]), FixCheckSpend.None);
        }

        if (workspace.DiscoveryProblem is string incomplete)
        {
            await context.Error.WriteLineAsync($"lockflight: the reproductions are not run, so their findings stay unverified: {incomplete}");
            return (FindingLifecycle.CombineReproductions(findings, [.. checks.Select(Reproductions.Unjudged)]), FixCheckSpend.None);
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> listed || workspace.Apps.Count == 0)
        {
            return (FindingLifecycle.CombineReproductions(findings, [.. checks.Select(Reproductions.Unjudged)]), FixCheckSpend.None);
        }

        IReadOnlyList<ReviewFinding> judged = FindingLifecycle.CombineReproductions(
            findings, await Reproductions.ConfirmAsync(checks, workspace.Apps, manifest.Root, timeLimit, repairer, repairable, cancellationToken));
        int fixChecks = judged.Count(finding => FixChecks.IsCheckable(finding, repairable));
        if (fixChecks == 0)
        {
            return (judged, FixCheckSpend.None);
        }

        DateTimeOffset started = context.Time.GetUtcNow();
        IReadOnlyList<ReviewFinding> fixChecked = await FixChecks.CheckAsync(
            judged,
            workspace.Apps,
            ChangeTests.Find(diff, manifest.Root, workspace.Apps, listed),
            ChangeTests.Files(diff, manifest.Root, workspace.Apps),
            manifest.Root,
            timeLimit,
            repairer,
            repairable,
            cancellationToken);
        return (fixChecked, new FixCheckSpend(fixChecks, (long)(context.Time.GetUtcNow() - started).TotalMilliseconds));
    }

    /// <summary>Records the run and every finding it fixed in the ledger, and turns the open findings into the exit code.</summary>
    private async Task<ExitCode> RecordAsync(Manifest manifest, ReviewRun run, ReviewOutcome outcome, CancellationToken cancellationToken)
    {
        int openConfirmed = outcome.Findings.Count(finding => finding.State == FindingState.Open && finding.Outcome == FindingOutcome.Confirmed);
        int openUnverified = outcome.Findings.Count(finding => finding.State == FindingState.Open && finding.Outcome != FindingOutcome.Confirmed);
        ReviewFinding[] fixedFindings = [.. outcome.Findings.Where(finding => finding.State == FindingState.Fixed)];
        string signature = openConfirmed > 0 ? LedgerSignatures.ReviewChanges
            : openUnverified > 0 ? LedgerSignatures.ReviewUnverified
            : run.OfRecord ? LedgerSignatures.ReviewClean
            : LedgerSignatures.ReviewNotOfRecord;
        LedgerWriter ledger = new(manifest.Root, context.Time);
        await ledger.AppendAsync(
            new LedgerEvent(
                LedgerEventType.ReviewCompleted,
                signature,
                $"{run.Feature}: run {run.Id}, {(run.OfRecord ? "of record" : "not of record")}, {outcome.Findings.Count} findings, "
                    + $"{openConfirmed} open confirmed and {openUnverified} open unverified or contested blocker/major, {fixedFindings.Length} fixed, ${Cost(outcome)}",
                null)
            {
                Review = new LedgerReview(
                    run.Feature, run.Id, run.OfRecord, outcome.Findings.Count, openConfirmed, openUnverified, fixedFindings.Length,
                    outcome.Usage.InputTokens, outcome.Usage.OutputTokens, outcome.CostUsd),
            },
            cancellationToken);
        foreach (ReviewFinding finding in fixedFindings)
        {
            await ledger.AppendAsync(
                new LedgerEvent(LedgerEventType.FindingFixed, LedgerSignatures.FindingFixed, $"{run.Feature} {finding.Id} fixed", null)
                {
                    Finding = new LedgerFinding(run.Feature, finding.Id, ReviewNames.ToName(finding.Severity), finding.Dimension),
                },
                cancellationToken);
        }

        // An unverified blocker/major is not a pass, and neither is a clean run that is not the review of record: a person has to look.
        return openConfirmed > 0 ? ExitCode.Fail : openUnverified > 0 || !run.OfRecord ? ExitCode.NotEarned : ExitCode.Pass;
    }

    private async Task PrintAsync(ReviewRun run, ReviewOutcome outcome, string root, string evidence)
    {
        await context.Output.WriteLineAsync($"Reviewer verdict: {outcome.Verdict} — {outcome.Summary}");
        foreach (ReviewFinding finding in outcome.Findings.OrderBy(finding => finding.Severity))
        {
            string state = finding.State is FindingState findingState ? $", {ReviewNames.ToName(findingState)}" : string.Empty;
            string requirement = finding.Requirement is null ? string.Empty : $" ({finding.Requirement})";
            string carried = finding.Carried ? ", carried from an earlier run" : string.Empty;
            string notes = finding.NeedsReproduction ? $"; {string.Join("; ", ReviewSummary.Notes(finding))}" : string.Empty;
            await context.Output.WriteLineAsync(
                $"- {ReviewNames.ToName(finding.Severity)} [{ReviewNames.ToName(finding.Outcome)}{state}] {finding.File}:{finding.Line}{requirement} {finding.Title} ({finding.Id}{carried}{notes})");
            if (finding.Outcome == FindingOutcome.Contested && finding.FixCheckErrors is string errors)
            {
                await context.Output.WriteLineAsync($"  {errors.ReplaceLineEndings(" ")}");
            }
        }

        await context.Output.WriteLineAsync(
            $"Cost ${Cost(outcome)} (estimate), {outcome.Usage.InputTokens} input and {outcome.Usage.OutputTokens} output tokens, {outcome.DurationMs / 1000} s. "
            + $"Evidence: {Path.GetRelativePath(root, evidence)}");
        if (outcome.FixChecks.Count > 0)
        {
            await context.Output.WriteLineAsync(
                $"Fix checks: {outcome.FixChecks.Count}, {outcome.FixChecks.WallTimeMs / 1000.0:0.0} s. A contested finding's fix contradicts the change's own tests: "
                + "a person decides whether the finding or the test is right.");
        }

        await context.Output.WriteLineAsync(run.OfRecord
            ? $"This run is the review of record (base {run.Base.Ref} is the merge base of HEAD and {run.Base.Target})."
            : $"This run is recorded but is not the review of record: {string.Join("; ", run.NotOfRecord)}.");
        int open = outcome.Findings.Count(finding => finding.State == FindingState.Open);
        await context.Output.WriteLineAsync(
            $"Open blocker/major findings: {open}. They stay open until a later review re-runs their reproduction and it passes, or a person "
            + "dismisses them; 'lockflight review status' shows them and the pull request summary.");
    }

    /// <summary>A blocker/major finding with its spec quote checked against the spec it was reviewed against; others as they are.</summary>
    private static ReviewFinding Anchor(ReviewFinding finding, string requirements) =>
        finding.NeedsReproduction ? finding with { SpecAnchor = SpecQuotes.Check(finding.SpecQuote, requirements) } : finding;

    private static string Cost(ReviewOutcome outcome) => outcome.CostUsd.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool TryParseBudget(string text, out decimal budget) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out budget) && budget is > 0 and <= ReviewSettings.MaxBudgetUsd;
}
