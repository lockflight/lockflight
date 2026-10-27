using System.Globalization;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Review;

/// <summary>
/// <c>lockflight review status [--feature id]</c> — read only: the feature's review runs, its open, fixed and dismissed findings,
/// and the summary for the pull request body. The agent runs it; only this, not the agent's own reading, says a finding is closed.
/// </summary>
internal sealed class ReviewStatusCommand(CliContext context)
{
    private static readonly string[] ValueOptions = ["--feature"];

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("review status", args, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options
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

        ReviewHistory history = ReviewEvidence.Load(feature.RecordDirectory);
        TextWriter output = context.Output;
        int ofRecord = history.Runs.Count(run => run.OfRecord);
        await output.WriteLineAsync($"# Review status: {feature.Id}\n");
        await output.WriteLineAsync($"{ReviewSummary.Runs(history.Runs.Count)} ({ofRecord} of record).");
        foreach (ReviewRunSummary run in history.Runs)
        {
            string record = run.OfRecord ? "of record" : $"not of record: {string.Join("; ", run.NotOfRecord)}";
            await output.WriteLineAsync($"- {run.Id} {run.Model}, base {run.BaseRef}, ${run.EstimatedCostUsd.ToString("0.000", CultureInfo.InvariantCulture)} (estimate), {record}");
        }

        await output.WriteLineAsync($"\nOpen findings ({history.Open.Count}):");
        foreach (TrackedFinding tracked in history.Open)
        {
            await output.WriteLineAsync($"- {ReviewSummary.Line(tracked.Finding)} (open since run {tracked.FirstRun})");
        }

        await output.WriteLineAsync($"\nFixed ({history.Fixed.Count}):");
        foreach (TrackedFinding tracked in history.Fixed)
        {
            await output.WriteLineAsync($"- {ReviewSummary.Line(tracked.Finding)} (reproduction passed in run {tracked.LastRun})");
        }

        await output.WriteLineAsync($"\nDismissed ({history.Dismissed.Count}):");
        foreach (FindingDismissal dismissal in history.Dismissed)
        {
            await output.WriteLineAsync(
                $"- `{dismissal.FindingId}` {ReviewNames.ToName(dismissal.Severity)}: `{dismissal.File}:{dismissal.Line}` {dismissal.Title} — "
                + $"dismissed by {dismissal.By} on {dismissal.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {dismissal.Reason}");
        }

        await output.WriteLineAsync("\n## Pull request summary\n");
        foreach (string line in ReviewSummary.PullRequest(history))
        {
            await output.WriteLineAsync(line);
        }

        return ExitCode.Pass;
    }
}
