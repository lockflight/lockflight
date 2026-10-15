using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>lockflight approve spec|tasks [--feature id] [--sign] [--key file]</c> records a person's approval of the feature's artifacts
/// (human-only). <c>lockflight approve verify (--feature id | --base ref) [--allowed-signers file] [--require spec,tasks]</c> checks
/// records, hashes and — with an allowed-signers file read from the base branch — SSH signatures for the named feature or every
/// feature the change touches; every phase is required unless <c>--require</c> narrows it. It is what CI runs.
/// </summary>
internal sealed class ApproveCommand(CliContext context)
{
    private const string UnknownApprover = "unknown";
    private const string VerifyVerb = "verify";

    private static readonly string[] ApproveValueOptions = ["--feature", "--key"];
    private static readonly string[] ApproveFlags = ["--sign"];
    private static readonly string[] VerifyValueOptions = ["--feature", "--base", "--allowed-signers", "--require"];

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string verb = args[0];
        bool verify = verb == VerifyVerb;
        if (await CommandOptions.ParseOrReportAsync(
                $"approve {verb}", args[1..], verify ? VerifyValueOptions : ApproveValueOptions, verify ? [] : ApproveFlags, context.Error, cancellationToken)
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

        string approvedBy = await Git.ConfigValueAsync(manifest.Root, "user.email", cancellationToken) ?? UnknownApprover;
        ApprovalStore store = new(feature);
        string record = store.Save(phase, approvedBy, context.Time.GetUtcNow(), ToolPin.RunningVersion);
        await context.Output.WriteLineAsync($"Approved {ApprovalPhaseNames.ToName(phase)} of {feature.Id} as {approvedBy}: {Path.GetRelativePath(manifest.Root, record)}");

        if (options.Flag("--sign") && await SignAsync(manifest.Root, record, options.Value("--key"), cancellationToken) is ExitCode failure)
        {
            return failure;
        }

        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(
                LedgerEventType.ConfigWritten,
                LedgerSignatures.ApprovePrefix + ApprovalPhaseNames.ToName(phase),
                $"{feature.Id} approved by {approvedBy}",
                null),
            cancellationToken);
        return ExitCode.Pass;
    }

    /// <summary>Signs the record with the person's SSH key; null when signed, else the exit code of the failure (already reported).</summary>
    private async Task<ExitCode?> SignAsync(string root, string record, string? keyOption, CancellationToken cancellationToken)
    {
        string? key = keyOption ?? await Git.ConfigValueAsync(root, "user.signingkey", cancellationToken);
        if (key is null)
        {
            await context.Error.WriteLineAsync(
                "lockflight: --sign needs --key <file> or git config user.signingkey (an SSH key; prefer one that needs a touch to sign)");
            return ExitCode.Config;
        }

        // ssh-keygen asks the agent or the key's owner (Touch ID / security-key touch) here; the engine never sees the private key.
        ProcessResult signing = await ProcessRunner.RunAsync(
            "ssh-keygen", ["-Y", "sign", "-f", ExpandHome(key), "-n", ApprovalStore.SignatureNamespace, record], root, cancellationToken);
        if (signing.ExitCode != 0)
        {
            await context.Error.WriteLineAsync($"lockflight: signing failed: {signing.Combined.Trim()}");
            return ExitCode.Error;
        }

        await context.Output.WriteLineAsync($"Signed: {Path.GetRelativePath(root, record)}.sig");
        return null;
    }

    private static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path;
}
