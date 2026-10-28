using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>lockflight approve spec|tasks [--feature id] [--sign] [--key file]</c> records a person's approval of the feature's artifacts
/// (human-only), once the matching phase check passes (<see cref="ApprovalReadiness"/>). <c>lockflight approve verify (--feature id
/// | --base ref) [--allowed-signers file] [--require spec,tasks]</c> checks records, hashes and — with an allowed-signers file read
/// from the base branch — SSH signatures for the named feature or every feature the change touches; every phase is required unless
/// <c>--require</c> narrows it. It is what CI runs.
/// </summary>
internal sealed class ApproveCommand(CliContext context) : ICommand
{
    private const string VerifyVerb = "verify";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        (string verb, string[] rest) = arguments.Verb();
        bool verify = verb == VerifyVerb;
        if (await CommandOptions.ParseOrReportAsync($"approve {verb}", rest, arguments.Command.ValueOptions, arguments.Command.FlagOptions, context.Error, cancellationToken)
            is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        ApprovalPhase phase = default;
        if (!verify && !ApprovalPhaseNames.TryParse(verb, out phase))
        {
            await context.Error.WriteLineAsync($"lockflight: unknown approval '{verb}'; use spec, tasks or verify");
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        if (verify)
        {
            return await new ApprovalVerifier(context, manifest.Root).RunAsync(options, cancellationToken);
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight: {lookup.Error}");
            return ExitCode.Config;
        }

        return await ApproveAsync(manifest, feature, phase, options, cancellationToken);
    }

    private async Task<ExitCode> ApproveAsync(Manifest manifest, Feature feature, ApprovalPhase phase, CommandOptions options, CancellationToken cancellationToken)
    {
        string primary = Path.Combine(feature.SpecDirectory, ApprovalPhaseNames.ArtifactsOf(phase)[0]);
        if (!File.Exists(primary))
        {
            await context.Error.WriteLineAsync($"lockflight: {Path.GetRelativePath(manifest.Root, primary)} does not exist; nothing to approve");
            return ExitCode.Config;
        }

        if (ApprovalReadiness.Of(manifest, feature, phase) is { Ready: false } readiness)
        {
            await context.Error.WriteLineAsync($"lockflight: nothing was approved: {readiness.Explanation}.");
            return ExitCode.Fail;
        }

        ApprovalStore store = new(feature);
        ApprovalRecording recording = await new ApprovalRecorder(context, manifest.Root).RecordAsync(
            feature, phase, [.. store.CurrentHashes(phase)], options.Flag("--sign"), options.Value("--key"), ApprovalChannel.Terminal, cancellationToken);
        if (!recording.Recorded)
        {
            await context.Error.WriteLineAsync($"lockflight: {recording.Problem}");
            return recording.FailureCode;
        }

        string record = Path.GetRelativePath(manifest.Root, recording.RecordPath!);
        await context.Output.WriteLineAsync($"Approved {ApprovalPhaseNames.ToName(phase)} of {feature.Id} as {recording.ApprovedBy}: {record}");
        if (recording.Signed)
        {
            await context.Output.WriteLineAsync($"Signed: {record}.sig");
        }

        return ExitCode.Pass;
    }
}
