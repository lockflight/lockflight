using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// <c>lockflight review [--feature id] [--base ref] [--model name] [--budget usd]</c> — the review: the engine builds the prompt from
/// the spec and the diff, runs a read-only <c>claude -p</c> with a JSON schema, confirms blocker/major findings by running their
/// reproductions (and re-runs those of findings still open from earlier runs), and stores the run. A run counts as the review of
/// record only with the manifest's model and budget against a base whose commit is the merge base of HEAD and the target branch
/// (<c>review.target</c>, else <c>origin/HEAD</c>'s branch, else <c>main</c>; also the default base); others are recorded and say why not.
/// Exit 1 when an open blocker/major finding is confirmed, 5 (NOT-EARNED) when one is unverified or the run is not of record, 0
/// otherwise. Advisory (guardrail): it runs with the developer's credentials and the agent can skip it.
/// </summary>
internal sealed class ReviewCommand(CliContext context)
{
    private const int MaxDiffCharacters = 120_000;
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
        return await ReviewAsync(manifest, feature, claude, reviewBase, diff, options.Value("--model") ?? manifest.Review.Model, budget, cancellationToken);
    }

    /// <summary>Asks the reviewer, confirms its findings and the open ones, stores the run and turns the outcome into the exit code.</summary>
    private async Task<ExitCode> ReviewAsync(
        Manifest manifest, Feature feature, string claude, ReviewBase reviewBase, string diff, string model, decimal budget, CancellationToken cancellationToken)
    {
        DateTimeOffset started = context.Time.GetUtcNow();
        await context.Output.WriteLineAsync($"Reviewing {feature.Id} against {reviewBase.Ref} with {model} (budget ${ReviewOfRecord.Usd(budget)})…");
        string requirements = await File.ReadAllTextAsync(feature.SpecPath, cancellationToken);
        string prompt = ReviewPrompt.Build(requirements, Truncated(diff));
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
        IReadOnlyList<ReviewFinding> judged = await ConfirmFindingsAsync(manifest, feature, FindingLifecycle.Carry(outcome.Findings, history), cancellationToken);
        outcome = outcome with { Findings = FindingLifecycle.Resolve(judged, history) };
        ReviewRun run = new(
            ReviewEvidence.NewRunId(feature.RecordDirectory, started),
            started,
            feature.Id,
            reviewBase,
            await Git.HeadAsync(manifest.Root, cancellationToken) ?? Git.UnknownCommit,
            Sha256(diff),
            Sha256(prompt),
            model,
            budget,
            manifest.Review,
            claudeVersion,
            (long)(context.Time.GetUtcNow() - started).TotalMilliseconds,
            ReviewOfRecord.Reasons(manifest.Review, model, budget, reviewBase));
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
    /// local settings (no hooks, no plugins), <c>--strict-mcp-config</c> skips MCP servers, <c>--tools</c> leaves only file reads,
    /// and <c>--permission-mode dontAsk</c> denies anything that would ask. Bare mode (<c>--bare</c>) is chosen only when an API key
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
                [.. signIn, "-p", "--restricted", "--strict-mcp-config", "--tools", "Read,Grep,Glob", "--permission-mode", "dontAsk",
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
    /// findings stay unverified, because a finding nobody could check is not a pass.
    /// </summary>
    private static async Task<IReadOnlyList<ReviewFinding>> ConfirmFindingsAsync(Manifest manifest, Feature feature, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReviewFinding> checks = FindingLifecycle.WithEarlierReproductions(findings);
        if (!checks.Any(Reproductions.IsRunnable))
        {
            return FindingLifecycle.CombineReproductions(findings, [.. checks.Select(Reproductions.Unjudged)]);
        }

        TimeSpan timeLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        TestWorkspace workspace = TestWorkspaces.For(manifest, feature);
        IReadOnlyList<ReviewFinding> judged = await workspace.BuildAsync(cancellationToken) is null && await workspace.ListAsync(cancellationToken) is not null && workspace.Apps.Count > 0
            ? await Reproductions.ConfirmAsync(checks, workspace.Apps[0], manifest.Root, timeLimit, cancellationToken)
            : [.. checks.Select(Reproductions.Unjudged)];
        return FindingLifecycle.CombineReproductions(findings, judged);
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
                    + $"{openConfirmed} open confirmed and {openUnverified} open unverified blocker/major, {fixedFindings.Length} fixed, ${Cost(outcome)}",
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
            await context.Output.WriteLineAsync(
                $"- {ReviewNames.ToName(finding.Severity)} [{ReviewNames.ToName(finding.Outcome)}{state}] {finding.File}:{finding.Line}{requirement} {finding.Title} ({finding.Id}{carried})");
        }

        await context.Output.WriteLineAsync(
            $"Cost ${Cost(outcome)} (estimate), {outcome.Usage.InputTokens} input and {outcome.Usage.OutputTokens} output tokens, {outcome.DurationMs / 1000} s. "
            + $"Evidence: {Path.GetRelativePath(root, evidence)}");
        await context.Output.WriteLineAsync(run.OfRecord
            ? $"This run is the review of record (base {run.Base.Ref} is the merge base of HEAD and {run.Base.Target})."
            : $"This run is recorded but is not the review of record: {string.Join("; ", run.NotOfRecord)}.");
        int open = outcome.Findings.Count(finding => finding.State == FindingState.Open);
        await context.Output.WriteLineAsync(
            $"Open blocker/major findings: {open}. They stay open until a later review re-runs their reproduction and it passes, or a person "
            + "dismisses them; 'lockflight review status' shows them and the pull request summary.");
    }

    private static string Truncated(string diff) =>
        diff.Length > MaxDiffCharacters ? diff[..MaxDiffCharacters] + "\n[diff truncated; read the files]" : diff;

    private static string Cost(ReviewOutcome outcome) => outcome.CostUsd.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static bool TryParseBudget(string text, out decimal budget) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out budget) && budget is > 0 and <= ReviewSettings.MaxBudgetUsd;
}
