using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Review;

/// <summary>
/// <c>lockflight review dismiss &lt;finding-id&gt; --reason "…" [--feature id]</c> — a person closes an open review finding, and the
/// dismissal records who (git <c>user.email</c>), when and why. Human-only like <c>approve</c>: refused inside a Claude Code session
/// (a guardrail; the variable can be unset), so the agent cannot drop a finding it would rather not fix.
/// </summary>
internal sealed class ReviewDismissCommand(CliContext context)
{
    private const string UnknownPerson = "unknown";
    private const string Usage = "lockflight review dismiss <finding-id> --reason \"<why it is not a defect>\" [--feature <id>]";

    private static readonly string[] ValueOptions = ["--reason", "--feature"];

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args is not [string id, .. string[] rest] || id.StartsWith('-'))
        {
            await context.Error.WriteLineAsync($"lockflight review dismiss: name the finding to dismiss: {Usage}");
            return ExitCode.Config;
        }

        if (await CommandOptions.ParseOrReportAsync("review dismiss", rest, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (options.Value("--reason")?.Trim() is not { Length: > 0 } reason)
        {
            await context.Error.WriteLineAsync($"lockflight review dismiss: --reason is required: say why the finding is not a defect ({Usage})");
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight: {lookup.Error}");
            return ExitCode.Config;
        }

        ReviewHistory history = ReviewEvidence.Load(feature.RecordDirectory);
        if (history.Open.FirstOrDefault(tracked => tracked.Finding.Id == id) is not TrackedFinding tracked)
        {
            string why = history.IsDismissed(id) ? "it is already dismissed"
                : history.Fixed.Any(fixedFinding => fixedFinding.Finding.Id == id) ? "it is fixed"
                : "'lockflight review status' lists the open ones";
            await context.Error.WriteLineAsync($"lockflight review dismiss: no open finding {id} in {feature.Id}; {why}");
            return ExitCode.Config;
        }

        ReviewFinding finding = tracked.Finding;
        string person = await Git.ConfigValueAsync(manifest.Root, "user.email", cancellationToken) ?? UnknownPerson;
        FindingDismissal dismissal = new(finding.Id, finding.Severity, finding.File, finding.Line, finding.Title, person, context.Time.GetUtcNow(), reason);
        string record = FindingDismissals.Save(feature.RecordDirectory, dismissal);
        // Who dismissed it and why stay in the record; the ledger is readable by the agent and needs only which finding closed.
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.FindingDismissed, LedgerSignatures.FindingDismissed, $"{feature.Id} {finding.Id} dismissed", null)
            {
                Finding = new LedgerFinding(feature.Id, finding.Id, ReviewNames.ToName(finding.Severity), finding.Dimension),
            },
            cancellationToken);
        await context.Output.WriteLineAsync(
            $"Dismissed {finding.Id} ({ReviewNames.ToName(finding.Severity)}: {finding.Title}). Recorded in {Path.GetRelativePath(manifest.Root, record)}; commit it with the change.");
        return ExitCode.Pass;
    }
}
