using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle;

/// <summary><c>lockflight phase check specify|tasks [--feature id]</c>: deterministic phase-exit checks; exit 0 pass, 1 fail.</summary>
internal sealed class PhaseCommand(CliContext context)
{
    private static readonly string[] ValueOptions = ["--feature"];

    public async Task<ExitCode> CheckAsync(string phase, string[] args, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync("phase check", args, ValueOptions, [], context.Error, cancellationToken) is not CommandOptions options
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

        if (PhaseChecks.Run(phase, feature, manifest) is not IReadOnlyList<PhaseCheckItem> items)
        {
            await context.Error.WriteLineAsync($"lockflight: unknown phase '{phase}'; use specify or tasks (implementation is checked by 'lockflight redgreen')");
            return ExitCode.Config;
        }

        await context.Output.WriteLineAsync($"Feature: {feature.Id} · phase: {phase}");
        foreach (PhaseCheckItem item in items)
        {
            await context.Output.WriteLineAsync($"{(item.Passed ? "PASS" : "FAIL")} {item.Name}: {item.Detail}");
        }

        return items.All(item => item.Passed) ? ExitCode.Pass : ExitCode.Fail;
    }
}
