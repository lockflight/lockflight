using Lockflight.Cli.Config;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What every red/green step works with — the console, the project, the feature and its test workspace — and how a step ends: a
/// verdict about the tests (FAIL, exit 1) or a broken tool that says nothing about them (ERROR, exit 3).
/// </summary>
internal sealed class RedGreenSession(CliContext context, Manifest manifest, Feature feature, TestWorkspace workspace)
{
    public const int MaxTestsListed = 5;

    private const int MaxErrorLines = 10;
    private const int ShortCommitLength = 8;

    public CliContext Context => context;

    public Manifest Manifest => manifest;

    public Feature Feature => feature;

    public TestWorkspace Workspace => workspace;

    public TextWriter Output => context.Output;

    public string RecordPath => RedGreenRecord.PathFor(feature);

    public void Save(RedGreenRecord record) => record.Save(RecordPath, feature.Id);

    public Task RecordAsync(string signature, string summary, CancellationToken cancellationToken) =>
        RecordAsync(signature, summary, detail: null, cancellationToken);

    public async Task RecordAsync(string signature, string summary, string? detail, CancellationToken cancellationToken) =>
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(new LedgerEvent(LedgerEventType.EvidenceRecorded, signature, summary, detail), cancellationToken);

    /// <summary>Says which <c>test.stopFilter</c> values a regression pass could not apply, so it ran every test of those projects.</summary>
    public async Task WarnAboutUnappliedFiltersAsync(RegressionRun regression, CancellationToken cancellationToken)
    {
        foreach (string filter in regression.Unsupported)
        {
            await context.Output.WriteLineAsync(
                $"WARN test.stopFilter '{filter}' is not one trait condition (Name=Value or Name!=Value), so red/green's regression pass ran every test.".AsMemory(),
                cancellationToken);
        }
    }

    /// <summary>The step judged the tests and refused (exit 1).</summary>
    public async Task<ExitCode> FailAsync(string message, CancellationToken cancellationToken)
    {
        await context.Error.WriteLineAsync($"lockflight: {message}".AsMemory(), cancellationToken);
        return ExitCode.Fail;
    }

    /// <summary>The step cannot judge the tests it was given, so nothing is earned (exit 5); the message says what to do.</summary>
    public async Task<ExitCode> NotEarnedAsync(string message, CancellationToken cancellationToken)
    {
        await context.Output.WriteLineAsync($"NOT-EARNED {message}".AsMemory(), cancellationToken);
        return ExitCode.NotEarned;
    }

    /// <summary>A tool the step needs could not list, build or run the tests: no verdict about them (exit 3).</summary>
    public async Task<ExitCode> BrokenAsync(string message, CancellationToken cancellationToken)
    {
        string text = $"lockflight: ERROR {message}; this says nothing about the tests — fix the tool problem and run the step again";
        await context.Error.WriteLineAsync(text.AsMemory(), cancellationToken);
        return ExitCode.Error;
    }

    /// <summary>
    /// Approvals bind the artifacts: red/green builds only on a spec and tasks that someone approved and nobody changed
    /// since. A missing record (never approved, deleted, or without artifact hashes) refuses like a stale one: fail closed.
    /// </summary>
    public string? UnapprovedArtifacts() => UnapprovedArtifacts(NotApproved());

    /// <summary>The phases whose approval is not valid now (missing, stale or a record that approves nothing).</summary>
    public IReadOnlyList<ApprovalCheck> NotApproved()
    {
        ApprovalStore approvals = new(feature);
        return [.. new[] { approvals.Check(ApprovalPhase.Spec), approvals.Check(ApprovalPhase.Tasks) }.Where(check => check.State != ApprovalState.Valid)];
    }

    /// <summary>
    /// Refuses the step because <paramref name="notValid"/> phases are not approved: writes the blocked report a person reads
    /// (<c>.lockflight/local/blocked.json</c>) and fails with the message.
    /// </summary>
    public async Task<ExitCode> RefuseUnapprovedAsync(string command, IReadOnlyList<ApprovalCheck> notValid, CancellationToken cancellationToken)
    {
        string? head = await Git.HeadAsync(manifest.Root, cancellationToken);
        BlockedReportFile.WriteLocal(manifest.Root, ApprovalBlockedReport.For($"engine:{command}", manifest.Root, feature, notValid, context.Time.GetUtcNow(), head));
        return await FailAsync(UnapprovedArtifacts(notValid)!, cancellationToken);
    }

    private static string? UnapprovedArtifacts(IReadOnlyList<ApprovalCheck> notValid) =>
        notValid.Count == 0
            ? null
            : $"{string.Join("; ", notValid.Select(check => check.Describe()))} (red/green works from approved artifacts only)";

    /// <summary>Test project directories relative to the root, '/'-separated; a project at the root is "." (locked as <c>**</c>).</summary>
    public string[] RelativeProjects() =>
        [.. workspace.Apps.Select(app => Path.GetRelativePath(manifest.Root, app.ProjectDirectory).Replace('\\', '/')).Distinct(StringComparer.Ordinal)];

    public static string Short(string commit) => commit.Length > ShortCommitLength ? commit[..ShortCommitLength] : commit;

    /// <summary>The build output's error lines, without repeats.</summary>
    public static string Tail(string output) =>
        string.Join('\n', output.Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Distinct().Take(MaxErrorLines));
}
