using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Ci;

/// <summary>
/// <c>lockflight ci review --head &lt;sha&gt; --base &lt;sha&gt; --out &lt;dir&gt; [--branch &lt;name&gt;]</c>: the review in its own
/// CI job, the only one that holds an API key. It runs in a clean checkout of the head (the reviewer reads the head's tree as it
/// is), takes the model, budget and diff limit from the target branch's manifest with <c>git show</c>, reviews against the merge
/// base, reads no earlier run or red/green record from the tree, and writes the run (<c>review.json</c>) and the result the report
/// joins (<c>review-result.json</c>) to <c>--out</c>. PR code runs in this job too (reproductions), so the key can be reached by it.
/// </summary>
internal sealed class CiReviewCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (options.Value(JudgeInputsReader.OutOption) is not string outDirectory)
        {
            await context.Error.WriteLineAsync($"lockflight ci review: {JudgeInputsReader.OutOption} <dir> is required (outside the checkout)");
            return ExitCode.Config;
        }

        JudgeInputsReading reading = await JudgeInputsReader.ReadAsync(options, context.WorkingDirectory, cancellationToken);
        if (reading.Inputs is not { Base: string baseCommit } inputs)
        {
            await context.Error.WriteLineAsync($"lockflight ci review: {reading.Error ?? $"{JudgeInputsReader.BaseOption} <sha> is required"}");
            return ExitCode.Config;
        }

        if (await JudgeGit.ResolveAsync(inputs.Root, "HEAD", cancellationToken) != inputs.Head)
        {
            await context.Error.WriteLineAsync($"lockflight ci review: the checkout is not at {inputs.Head}; check out the pull request's head commit");
            return ExitCode.Config;
        }

        string output = Path.GetFullPath(outDirectory, context.WorkingDirectory);
        (ExitCode exitCode, string why) = await ReviewAsync(inputs, baseCommit, output, cancellationToken);
        CheckVerdict verdict = exitCode switch
        {
            ExitCode.Pass => CheckVerdict.Pass,
            ExitCode.Fail => CheckVerdict.Fail,
            ExitCode.NotEarned => CheckVerdict.NotEarned,
            _ => CheckVerdict.Error,
        };
        JsonFiles.Write(Path.Combine(output, CiReportCommand.ReviewFileName), new JsonObject
        {
            ["schema"] = "lockflight.ci-review/v1",
            ["head"] = inputs.Head,
            ["verdict"] = CheckVerdictNames.ToName(verdict),
            ["why"] = CiEvidence.Cap(why),
        });
        return exitCode;
    }

    private async Task<(ExitCode ExitCode, string Why)> ReviewAsync(JudgeInputs inputs, string baseCommit, string output, CancellationToken cancellationToken)
    {
        BasePolicyReading policy = await BasePolicyReader.ReadAsync(inputs.Root, baseCommit, cancellationToken);
        if (policy.Policy is not BasePolicy basePolicy)
        {
            return (ExitCode.Config, policy.Error!);
        }

        IReadOnlyList<string> mergeBases = await JudgeGit.MergeBasesAsync(inputs.Root, baseCommit, inputs.Head, cancellationToken);
        if (mergeBases.Count == 0)
        {
            return (ExitCode.Config, "the head and the target branch share no history in this clone (fetch the full history)");
        }

        string mergeBase = mergeBases[0];
        IReadOnlyList<string> changed = await JudgeGit.ChangedFilesAsync(inputs.Root, mergeBase, inputs.Head, cancellationToken) ?? [];
        (string? featureId, string source) = await TreeSpecSource.FindAsync(inputs.Root, inputs.Head, inputs.Branch, changed, cancellationToken);
        if (featureId is null)
        {
            return (ExitCode.NotEarned, "no spec found for this pull request to review against");
        }

        Manifest manifest = (basePolicy.Manifest ?? new Manifest(inputs.Root, [], StopGate: false, StopTarget: null, GateSettings.Default));
        Feature feature = new(featureId, Path.Combine(inputs.Root, FeatureLocator.SpecsDirectory, featureId), Path.Combine(output, featureId));
        using StringWriter printed = new();
        CliContext review = context with { Output = new TeeWriter(context.Output, printed) };
        ExitCode exitCode = await new ReviewCommand(review).ReviewForCiAsync(manifest, feature, new ReviewBase(mergeBase, mergeBase, baseCommit, mergeBase), cancellationToken);
        string line = printed.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(text => text.StartsWith("Open blocker/major", StringComparison.Ordinal) || text.StartsWith("NOT-EARNED", StringComparison.Ordinal) || text.StartsWith("ERROR", StringComparison.Ordinal))
            ?? $"{source}: review exited {(int)exitCode}";
        return (exitCode, line);
    }
}
