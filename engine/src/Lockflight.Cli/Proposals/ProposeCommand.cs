using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// <c>lockflight propose &lt;path&gt; (--from &lt;file&gt; | --delete) […]</c> — turns a change the agent may not make itself (a
/// protected file) into one patch per file under <c>.lockflight/local/proposals/</c>, and prints for each whether the file is
/// protected, whether the change is high risk, its diffstat and the <c>git apply</c> command for the person. It applies nothing:
/// the person reads the patch and applies it, and the guard still blocks the agent's own <c>git apply</c>. Every request is
/// checked before any patch is written, so a refused pair leaves no partial set behind.
/// </summary>
internal sealed class ProposeCommand(CliContext context)
{
    private const string Command = "propose";

    public async Task<ExitCode> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        ProposalArgumentsParse parse = ProposalArguments.Parse(args);
        if (parse.Requests is not IReadOnlyList<ProposalRequest> requests)
        {
            await context.Error.WriteLineAsync($"lockflight {Command}: {parse.Error}. Run 'lockflight --help' for its options.".AsMemory(), cancellationToken);
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        string root = PathRules.Canonicalize(manifest.Root, baseDirectory: null);
        if (await ResolveAllAsync(requests, root, DefaultProtectedPaths.For(manifest), cancellationToken) is not IReadOnlyList<ProposalTarget> targets)
        {
            return ExitCode.Config;
        }

        List<(ProposalTarget Target, byte[] Patch)> patches = [];
        foreach (ProposalTarget target in targets)
        {
            PatchBuild build = await PatchBuilder.BuildAsync(target, cancellationToken);
            if (build.Patch is not byte[] patch)
            {
                await context.Error.WriteLineAsync($"lockflight {Command}: could not make a patch for '{target.RelativePath}': {build.Error}. No patch was written.".AsMemory(), cancellationToken);
                return ExitCode.Error;
            }

            patches.Add((target, patch));
        }

        LedgerWriter ledger = new(manifest.Root, context.Time);
        DateTimeOffset now = context.Time.GetLocalNow();
        foreach ((ProposalTarget target, byte[] patch) in patches)
        {
            string patchPath = ProposalFiles.NewPath(manifest.Root, now, target.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(patchPath)!);
            await File.WriteAllBytesAsync(patchPath, patch, cancellationToken);
            await ReportAsync(target, DiffStat.Of(patch), Path.GetRelativePath(manifest.Root, patchPath).Replace('\\', '/'), cancellationToken);
            await ledger.AppendAsync(LedgerEntry(target), cancellationToken);
        }

        await context.Output.WriteLineAsync(
            $"Nothing was applied. Give the person the command above for each patch, from {manifest.Root}; they read the patch and apply it.".AsMemory(), cancellationToken);
        return ExitCode.Pass;
    }

    /// <summary>Every request checked, or null after each refusal is reported.</summary>
    private async Task<IReadOnlyList<ProposalTarget>?> ResolveAllAsync(
        IReadOnlyList<ProposalRequest> requests, string root, IReadOnlyList<string> protectedGlobs, CancellationToken cancellationToken)
    {
        List<ProposalTarget> targets = [];
        List<string> refusals = [];
        foreach (ProposalRequest request in requests)
        {
            ProposalTargetResolution resolution = ProposalTargets.Resolve(request, root, protectedGlobs, context.WorkingDirectory);
            if (resolution.Target is not ProposalTarget target)
            {
                refusals.Add(resolution.Error!);
            }
            else if (targets.Any(earlier => string.Equals(earlier.RelativePath, target.RelativePath, StringComparison.OrdinalIgnoreCase)))
            {
                refusals.Add($"'{target.RelativePath}' is named more than once; propose one new content per file");
            }
            else
            {
                targets.Add(target);
            }
        }

        foreach (string refusal in refusals)
        {
            await context.Error.WriteLineAsync($"lockflight {Command}: {refusal}.".AsMemory(), cancellationToken);
        }

        if (refusals.Count > 0)
        {
            await context.Error.WriteLineAsync($"lockflight {Command}: no patch was written.".AsMemory(), cancellationToken);
            return null;
        }

        return targets;
    }

    private async Task ReportAsync(ProposalTarget target, DiffStat stat, string patchPath, CancellationToken cancellationToken)
    {
        string protection = target.ProtectedRule is string rule ? $"protected by rule '{rule}'" : "not protected";
        string risk = target.HighRisk is string reason ? $"high risk — {reason}; read every line before applying" : "not high risk";
        await context.Output.WriteLineAsync(
            $"""
            {target.RelativePath} ({ProposalChangeNames.ToName(target.Change)}) {stat}
              {protection}
              {risk}
              patch: {patchPath}
              apply: git apply --check {patchPath} && git apply {patchPath}

            """.AsMemory(),
            cancellationToken);
    }

    private static LedgerEvent LedgerEntry(ProposalTarget target) => new(
        LedgerEventType.ProposalCreated,
        $"{LedgerSignatures.ProposalPrefix}{target.RelativePath}",
        $"{ProposalChangeNames.ToName(target.Change)} {target.RelativePath}",
        $"rule: {target.ProtectedRule ?? "none"}; high risk: {(target.HighRisk is null ? "no" : "yes")}");
}
