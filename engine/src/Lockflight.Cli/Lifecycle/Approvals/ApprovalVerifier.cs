using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// <c>lockflight approve verify (--feature id | --base ref) [--require spec,tasks]</c>: prints a PASS or FAIL
/// line per required phase of each feature and exits 1 when any fails. The feature is never guessed (Spec Kit's active-feature pointer
/// is agent-writable): it is named, or every feature the change since the base touches is verified.
/// </summary>
internal sealed class ApprovalVerifier(CliContext context, string root)
{
    public async Task<ExitCode> RunAsync(CommandOptions options, CancellationToken cancellationToken)
    {
        string? requested = options.Value("--feature");
        string? baseRef = options.Value("--base");
        if ((requested is null) == (baseRef is null))
        {
            await context.Error.WriteLineAsync("lockflight: approve verify needs exactly one of --feature <id> or --base <ref> (verifies every feature the change touches)");
            return ExitCode.Config;
        }

        if (await RequiredPhasesAsync(options.Value("--require")) is not IReadOnlyList<ApprovalPhase> required)
        {
            return ExitCode.Config;
        }

        IReadOnlyList<string>? ids = requested is not null ? [requested] : await TouchedFeaturesAsync(baseRef!, cancellationToken);
        if (ids is null)
        {
            await context.Error.WriteLineAsync($"lockflight: git diff against '{baseRef}' failed; is the checkout deep enough (fetch-depth: 0)?");
            return ExitCode.Error;
        }

        if (ids.Count == 0)
        {
            await context.Output.WriteLineAsync($"No feature changed since {baseRef}; nothing to verify (changes outside a feature need no approval).");
            return ExitCode.Pass;
        }

        IReadOnlySet<string> adopted = new AdoptedFeatures(root).Load();
        bool allGood = true;
        foreach (string id in ids)
        {
            // A feature that predates Lockflight needs no approval until it has Lockflight records of its own (adopt apply).
            if (requested is null && adopted.Contains(id) && !Directory.Exists(FeatureLocator.Resolve(root, id).Feature?.RecordDirectory ?? string.Empty))
            {
                await context.Output.WriteLineAsync($"NOTE {id}: predates Lockflight (adopted) — no approval required");
                continue;
            }

            // A touched feature whose directory is gone at HEAD was deleted or renamed away: nothing is left to approve, and a
            // renamed feature is verified under its new name, which the change touches too.
            if (requested is null && !FeatureLocator.Resolve(root, id).Exists)
            {
                await context.Output.WriteLineAsync($"NOTE {id}: removed (its {FeatureLocator.SpecsDirectory}/ directory no longer exists) — nothing to verify");
                continue;
            }

            allGood &= await VerifyFeatureAsync(id, required);
        }

        return allGood ? ExitCode.Pass : ExitCode.Fail;
    }

    /// <summary>
    /// Every phase unless <c>--require</c> names some; each named value must be a phase (ignoring case). Null — after saying why —
    /// for an empty or unknown value: a typo must not narrow the check to nothing.
    /// </summary>
    private async Task<IReadOnlyList<ApprovalPhase>?> RequiredPhasesAsync(string? requireOption)
    {
        if (requireOption is null)
        {
            return Enum.GetValues<ApprovalPhase>();
        }

        List<ApprovalPhase> phases = [];
        foreach (string name in requireOption.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!ApprovalPhaseNames.TryParse(name, out ApprovalPhase phase))
            {
                string known = string.Join(", ", Enum.GetValues<ApprovalPhase>().Select(ApprovalPhaseNames.ToName));
                await context.Error.WriteLineAsync($"lockflight: --require '{requireOption}' names '{name}', which is not a phase; use a comma-separated list of {known}");
                return null;
            }

            phases.Add(phase);
        }

        return [.. phases.Distinct()];
    }

    /// <summary>Prints a PASS or FAIL line per required phase of one feature; true when all pass.</summary>
    private async Task<bool> VerifyFeatureAsync(string id, IReadOnlyList<ApprovalPhase> required)
    {
        FeatureLookup lookup = FeatureLocator.Resolve(root, id);
        if (lookup.Feature is not Feature feature)
        {
            await context.Output.WriteLineAsync($"FAIL {id}: {lookup.Error}");
            return false;
        }

        ApprovalStore store = new(feature);
        bool allGood = true;
        foreach (ApprovalPhase phase in required)
        {
            ApprovalCheck check = store.Check(phase);
            bool valid = check.State == ApprovalState.Valid;
            allGood &= valid;
            await context.Output.WriteLineAsync($"{(valid ? "PASS" : "FAIL")} {feature.Id}: {check.Describe()}");
        }

        return allGood;
    }

    /// <summary>
    /// Features whose spec directory or Lockflight records changed since the base — deleting or renaming records counts as touching
    /// them. Null when git fails.
    /// </summary>
    private async Task<IReadOnlyList<string>?> TouchedFeaturesAsync(string baseRef, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? changed = await Git.ChangedNamesAsync(root, Git.SinceMergeBase(baseRef), Timeout.InfiniteTimeSpan, cancellationToken);
        return changed is null
            ? null
            : [.. changed
                .Select(FeatureLocator.FeatureIdOf)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
    }
}
