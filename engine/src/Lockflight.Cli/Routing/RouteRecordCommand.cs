using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// <c>lockflight route record [--feature &lt;id&gt;] [--base &lt;ref&gt;]</c>: classifies the current branch as <c>route show</c> does and
/// records the class in the feature's <c>class.json</c>, never lower than what is recorded there. Committed with the feature's
/// records, it raises the feature's later pull requests once it is on the base branch. A guardrail: a pull request can leave the
/// record out, or name another branch.
/// </summary>
internal sealed class RouteRecordCommand(CliContext context) : ICommand
{
    public const string FeatureOption = "--feature";
    public const string BaseOption = "--base";

    private const string DefaultBranch = "main";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options
            || await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value(FeatureOption));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight route record: {lookup.Error}");
            return ExitCode.Config;
        }

        string baseRef = options.Value(BaseOption) ?? await Git.DefaultBranchAsync(manifest.Root, DefaultBranch, cancellationToken);
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(manifest.Root, baseRef, headRef: null, cancellationToken);
        if (reading.ChangeSet is not ChangeSet changeSet)
        {
            await context.Error.WriteLineAsync($"lockflight route record: {reading.Message}");
            return reading.Status == ChangeSetReadStatus.NoBase ? ExitCode.Config : ExitCode.Error;
        }

        RaiseReading raises = await RaiseReader.ReadAsync(manifest.Root, changeSet, new RaiseInputs(GitBranch.Current(manifest.Root), [], Raise: null), cancellationToken);
        if (raises.Status != RaiseReadStatus.Read)
        {
            await context.Error.WriteLineAsync($"lockflight route record: {string.Join("; ", raises.Notes)}");
            return ExitCode.Error;
        }

        RoutingPolicy policy = RoutingPolicy.From(manifest, new PolicySource(baseRef, "working tree", ManifestBlob: null, RoutingPresetNames.ToName(manifest.Routing.Preset)));
        ChangeClass computed = ChangeClassifier.Classify(changeSet, policy, raises.Raises).Class;
        string path = Path.Combine(manifest.Root, FeatureClassRecord.PathFor(feature.Id).Replace('/', Path.DirectorySeparatorChar));
        ChangeClass? recorded = File.Exists(path) ? FeatureClassRecord.Parse(await File.ReadAllTextAsync(path, cancellationToken)) : null;
        ChangeClass written = recorded is ChangeClass previous && previous > computed ? previous : computed;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, FeatureClassRecord.Render(written), cancellationToken);
        await context.Output.WriteLineAsync(
            $"route record: feature {feature.Id} is {ChangeClassNames.ToName(written)}"
            + (written == computed ? string.Empty : $" (recorded before; this change computes {ChangeClassNames.ToName(computed)})")
            + $"; commit {FeatureClassRecord.PathFor(feature.Id)} with the feature's records");
        return ExitCode.Pass;
    }
}
