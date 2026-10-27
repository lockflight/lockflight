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
/// <c>lockflight review [--feature id] [--base ref] [--model opus] [--budget 3.00]</c> — the review of record: the
/// engine builds the prompt from the spec and the diff, runs a read-only <c>claude -p</c> with a JSON schema, confirms blocker/major
/// findings by running their reproductions, and stores the result. Exit 1 when a blocker/major finding is confirmed, 5 (NOT-EARNED)
/// when one could not be verified and needs a person, 0 otherwise.
/// Advisory (guardrail): it runs with the developer's credentials and the agent can skip it.
/// </summary>
internal sealed class ReviewCommand(CliContext context)
{
    private const int MaxDiffCharacters = 120_000;
    private const string DefaultBase = "main";
    private const string DefaultModel = "opus";
    private const string DefaultBudgetUsd = "3.00";

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

        if (Executables.Find("claude", explicitPath: null) is not string claude)
        {
            await context.Output.WriteLineAsync("NOT-EARNED claude is not on PATH; the review of record needs Claude Code signed in (subscription or API key)");
            return ExitCode.NotEarned;
        }

        string baseRef = options.Value("--base") ?? DefaultBase;
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

        return await ReviewAsync(manifest, feature, claude, baseRef, diff, options, cancellationToken);
    }

    /// <summary>Asks the reviewer, confirms its findings, stores the evidence and turns the outcome into the exit code.</summary>
    private async Task<ExitCode> ReviewAsync(
        Manifest manifest, Feature feature, string claude, string baseRef, string diff, CommandOptions options, CancellationToken cancellationToken)
    {
        string model = options.Value("--model") ?? DefaultModel;
        string budget = options.Value("--budget") ?? DefaultBudgetUsd;
        await context.Output.WriteLineAsync($"Reviewing {feature.Id} against {baseRef} with {model} (budget ${budget})…");
        string requirements = await File.ReadAllTextAsync(feature.SpecPath, cancellationToken);
        ProcessResult run = await AskReviewerAsync(manifest, claude, model, budget, ReviewPrompt.Build(requirements, Truncated(diff)), cancellationToken);
        ReviewOutcome outcome = ReviewResultParser.Parse(run.Output);
        if (outcome.Error is not null)
        {
            // A verdict line like the gates' ERROR: the review did not happen, which says nothing about the code.
            await context.Output.WriteLineAsync($"ERROR {outcome.Error}");
            return ExitCode.Error;
        }

        outcome = outcome with { Findings = await ConfirmFindingsAsync(manifest, feature, outcome.Findings, cancellationToken) };
        string head = await Git.HeadAsync(manifest.Root, cancellationToken) ?? Git.UnknownCommit;
        string diffHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(diff)));
        string evidence = ReviewEvidence.Save(feature.RecordDirectory, outcome, baseRef, head, diffHash, model, context.Time.GetUtcNow());
        await PrintAsync(outcome, manifest.Root, evidence);
        return await RecordAsync(manifest, feature, outcome, cancellationToken);
    }

    /// <summary>Runs a read-only <c>claude -p</c> with the review schema; the prompt goes through stdin.</summary>
    private static async Task<ProcessResult> AskReviewerAsync(
        Manifest manifest, string claude, string model, string budget, string prompt, CancellationToken cancellationToken)
    {
        // Not an argument: a diff easily exceeds Windows' 32 K command line and Linux's 128 KiB per-argument limit.
        string promptFile = Path.Combine(Path.GetTempPath(), $"lockflight-review-{Guid.CreateVersion7()}.md");
        try
        {
            await File.WriteAllTextAsync(promptFile, prompt, cancellationToken);
            return await ProcessRunner.RunAsync(
                claude,
                ["-p", "--restricted", "--strict-mcp-config", "--tools", "Read,Grep,Glob", "--model", model, "--json-schema", ReviewPrompt.Schema,
                 "--output-format", "json", "--no-session-persistence", "--max-budget-usd", budget],
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

    /// <summary>Runs the reproductions of blocker/major findings when the tests build and list; otherwise they stay unverified.</summary>
    private static async Task<IReadOnlyList<ReviewFinding>> ConfirmFindingsAsync(Manifest manifest, Feature feature, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken)
    {
        if (!findings.Any(finding => finding.NeedsReproduction))
        {
            return findings;
        }

        TimeSpan timeLimit = TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds);
        TestWorkspace workspace = TestWorkspaces.For(manifest, feature);
        return await workspace.BuildAsync(cancellationToken) is null && await workspace.ListAsync(cancellationToken) is not null && workspace.Apps.Count > 0
            ? await Reproductions.ConfirmAsync(findings, workspace.Apps[0], manifest.Root, timeLimit, cancellationToken)
            : findings;
    }

    /// <summary>Records the review in the ledger and turns it into the exit code.</summary>
    private async Task<ExitCode> RecordAsync(Manifest manifest, Feature feature, ReviewOutcome outcome, CancellationToken cancellationToken)
    {
        int confirmed = outcome.Findings.Count(finding => finding.NeedsReproduction && finding.Status == FindingStatus.Confirmed);
        int unverified = outcome.Findings.Count(finding => finding.NeedsReproduction && finding.Status == FindingStatus.Unverified);
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(
                LedgerEventType.EvidenceRecorded,
                confirmed > 0 ? LedgerSignatures.ReviewChanges : unverified > 0 ? LedgerSignatures.ReviewUnverified : LedgerSignatures.ReviewClean,
                $"{feature.Id}: {outcome.Findings.Count} findings, {confirmed} confirmed and {unverified} unverified blocker/major, ${Cost(outcome)}",
                null),
            cancellationToken);
        // An unverified blocker/major is not a pass: a person has to judge it.
        return confirmed > 0 ? ExitCode.Fail : unverified > 0 ? ExitCode.NotEarned : ExitCode.Pass;
    }

    private async Task PrintAsync(ReviewOutcome outcome, string root, string evidence)
    {
        await context.Output.WriteLineAsync($"Reviewer verdict: {outcome.Verdict} — {outcome.Summary}");
        foreach (ReviewFinding finding in outcome.Findings.OrderBy(finding => finding.Severity))
        {
            string status = finding.NeedsReproduction ? $" [{ReviewNames.ToName(finding.Status)}]" : string.Empty;
            string requirement = finding.Requirement is null ? string.Empty : $" ({finding.Requirement})";
            await context.Output.WriteLineAsync($"- {ReviewNames.ToName(finding.Severity)}{status} {finding.File}:{finding.Line}{requirement} {finding.Title}");
        }

        await context.Output.WriteLineAsync($"Cost ${Cost(outcome)}, {outcome.DurationMs / 1000} s. Evidence: {Path.GetRelativePath(root, evidence)}");
        await context.Output.WriteLineAsync("Confirmed findings must be fixed; unverified ones need a person's judgement; not-reproduced ones may be dismissed.");
    }

    private static string Truncated(string diff) =>
        diff.Length > MaxDiffCharacters ? diff[..MaxDiffCharacters] + "\n[diff truncated; read the files]" : diff;

    private static string Cost(ReviewOutcome outcome) => outcome.CostUsd.ToString("0.000", CultureInfo.InvariantCulture);
}
