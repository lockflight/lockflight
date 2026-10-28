using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// <c>lockflight adopt plan|apply|undo</c>: takes over an existing Claude Code and Spec Kit setup. <c>plan</c> only reads
/// and writes <c>.lockflight/adopt/plan.md</c>; <c>apply</c> and <c>undo</c> are human decisions, refused inside a Claude Code
/// session. Apply needs a clean working tree and the setup the plan was made from; it backs up first and rolls back on any failure.
/// Undo reverts what apply changed, and keeps files changed since (unless forced).
/// </summary>
internal sealed class AdoptCommand(CliContext context)
{
    private const int MaxCustomisedListed = 8;

    public async Task<ExitCode> RunAsync(string verb, string[] args, CancellationToken cancellationToken)
    {
        (string[] values, string[] flags) = verb switch
        {
            "undo" => (["--backup"], ["--force"]),
            "apply" => (["--speckit-preset"], ["--skip-speckit", "--no-sandbox"]),
            _ => ((string[])[], (string[])[]),
        };
        if (await CommandOptions.ParseOrReportAsync($"adopt {verb}", args, values, flags, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        if (await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        return verb switch
        {
            "plan" => await PlanAsync(manifest),
            "apply" => await ApplyAsync(manifest, options, cancellationToken),
            "undo" => await UndoAsync(manifest, options.Value("--backup"), options.Flag("--force"), cancellationToken),
            _ => await ReportAsync($"unknown adopt step '{verb}'; use plan, apply or undo", ExitCode.Config),
        };
    }

    private AdoptionPlan Inventory(Manifest manifest) =>
        SetupInventory.Build(manifest.Root, manifest, context.Environment.Get(EnvironmentVariables.Home), context.Time.GetUtcNow());

    private async Task<ExitCode> PlanAsync(Manifest manifest)
    {
        AdoptionPlan plan = Inventory(manifest);
        bool upgradesSpecKit = plan.Items.Any(item => item is { Area: AdoptionArea.SpecKit, Action: AdoptionAction.Upgrade });
        string? preset = upgradesSpecKit ? PresetLocator.Find(context.Environment.Get(EnvironmentVariables.Home), manifest.Root) : null;
        string engine = File.Exists(LinkCommand.BinaryPath(manifest.Root)) ? EngineCommand.Linked : EngineCommand.Name;
        string applyLine = AdoptApplyLine.For(manifest.Root, engine, upgradesSpecKit, preset);
        string? applyNote = AdoptApplyLine.Note(upgradesSpecKit, preset);
        AdoptionPlanWriter.Write(plan, applyLine, applyNote);
        foreach (IGrouping<AdoptionAction, AdoptionItem> action in plan.Items.GroupBy(item => item.Action).OrderBy(group => group.Key))
        {
            await context.Output.WriteLineAsync($"{action.Key.ToString().ToLowerInvariant(),-8} {action.Count()}");
        }

        await context.Output.WriteLineAsync(
            $"Plan: {AdoptionPlan.RelativeDirectory}/{AdoptionPlan.MarkdownFile}. Review it, then {HumanOnlyCommands.OpenATerminalAndRun(applyLine)}.");
        if (applyNote is not null)
        {
            await context.Output.WriteLineAsync(applyNote);
        }

        return ExitCode.Pass;
    }

    private async Task<ExitCode> ApplyAsync(Manifest manifest, CommandOptions options, CancellationToken cancellationToken)
    {
        string fingerprintPath = Path.Combine(manifest.Root, AdoptionPlan.RelativeDirectory, AdoptionPlanWriter.FingerprintFile);
        AdoptionPlan plan = Inventory(manifest);
        if (!File.Exists(fingerprintPath) || File.ReadAllText(fingerprintPath).Trim() != AdoptionPlanWriter.Fingerprint(plan))
        {
            return await ReportAsync("the setup is not the one the plan was made from (or there is no plan); run 'lockflight adopt plan' and review it first", ExitCode.Config);
        }

        ProcessResult status = await ProcessRunner.RunAsync("git", ["status", "--porcelain", "--untracked-files=normal"], manifest.Root, cancellationToken);
        if (status.ExitCode != 0 || status.Output.Trim().Length > 0)
        {
            return await ReportAsync("apply needs a clean working tree in a git repository (commit or stash first), so the change is easy to review and undo", ExitCode.Config);
        }

        string id = AdoptionBackup.NewId(manifest.Root, context.Time.GetUtcNow());
        string archive = AdoptionBackup.Create(manifest.Root, id);
        ProcessResult reference = await ProcessRunner.RunAsync("git", ["update-ref", $"refs/lockflight/backup/{id}", "HEAD"], manifest.Root, cancellationToken);
        await context.Output.WriteLineAsync($"Backup: {Path.GetRelativePath(manifest.Root, archive)}{(reference.ExitCode == 0 ? $" and git ref refs/lockflight/backup/{id}" : " (no git ref: git update-ref failed)")}");

        List<string> done = [];
        List<string> manual = [];
        try
        {
            if (await ApplyChangesAsync(manifest, plan, options, done, manual, cancellationToken) is string problem)
            {
                AdoptionBackup.RestoreAll(manifest.Root, id);
                return await ReportAsync($"{problem}; everything was restored from the backup", ExitCode.Error);
            }
        }
        catch (Exception exception)
        {
            // Any failure part-way (a write, the Spec Kit merge, Ctrl+C) rolls back to the backup: never a half-applied setup.
            AdoptionBackup.RestoreAll(manifest.Root, id);
            await context.Error.WriteLineAsync($"lockflight: apply failed ({exception.Message}); everything was restored from the backup");
            return ExitCode.Error;
        }

        AdoptionBackup.RecordApplied(manifest.Root, id);
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.ConfigWritten, "adopt:apply", string.Join("; ", done), null), cancellationToken);
        foreach (string line in done)
        {
            await context.Output.WriteLineAsync($"done     {line}");
        }

        foreach (string line in manual)
        {
            await context.Output.WriteLineAsync($"manual   {line}");
        }

        await context.Output.WriteLineAsync($"To undo: {HumanOnlyCommands.OpenATerminalAndRun($"{EngineCommand.Name} adopt undo")}. Then review and commit, and run '{EngineCommand.Name} doctor'.");
        return ExitCode.Pass;
    }

    /// <summary>Every change apply makes; a problem that needs a rollback is returned, an exception rolls back too.</summary>
    private static async Task<string?> ApplyChangesAsync(
        Manifest manifest, AdoptionPlan plan, CommandOptions options, List<string> done, List<string> manual, CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(manifest.Root, Manifest.FileName);
        string manifestText = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        string[] newGlobs = [.. plan.Items.Where(item => item.Action == AdoptionAction.Protect).SelectMany(item => item.Globs)
            .Distinct(StringComparer.Ordinal).Where(glob => !manifest.ProtectedGlobs.Contains(glob, StringComparer.Ordinal))];
        manifestText = Extend(manifestText, "protected", [.. newGlobs.Select(glob => (IReadOnlyList<string>)[$"\"{glob}\""])], done, manual, $"{newGlobs.Length} paths added to protected: {string.Join(", ", newGlobs)}");
        AdoptionItem[] units = [.. plan.Items.Where(item => item is { Area: AdoptionArea.Unit, Action: AdoptionAction.Add })];
        manifestText = Extend(manifestText, "units", [.. units.Select(UnitEntry)], done, manual, $"{units.Length} units added");
        await File.WriteAllTextAsync(manifestPath, manifestText, cancellationToken);
        if (ManifestLocator.Discover(manifest.Root).Error is ManifestError error)
        {
            return $"the edited manifest does not load ({error.Describe()})";
        }

        await AddProtectedPathSettingsAsync(manifest.Root, sandbox: !options.Flag("--no-sandbox"), done, manual, cancellationToken);

        string claudeMd = Path.Combine(manifest.Root, "CLAUDE.md");
        string claudeText = File.Exists(claudeMd) ? await File.ReadAllTextAsync(claudeMd, cancellationToken) : string.Empty;
        if (!ClaudeMdBlock.IsCurrent(claudeText))
        {
            await File.WriteAllTextAsync(claudeMd, ClaudeMdBlock.Upsert(claudeText), cancellationToken);
            done.Add(ClaudeMdBlock.IsPresent(claudeText) ? "Lockflight block in CLAUDE.md updated" : "Lockflight block added to CLAUDE.md");
        }

        string[] features = [.. plan.Items.Where(item => item is { Area: AdoptionArea.Feature, Action: AdoptionAction.Mark }).Select(item => item.Subject)];
        if (features.Length > 0)
        {
            AdoptedFeatures adopted = new(manifest.Root);
            adopted.Save(adopted.Load().Concat(features));
            done.Add($"{features.Length} existing features marked as predating Lockflight (.lockflight/baselines/{AdoptedFeatures.FileName})");
        }

        if (plan.Items.Any(item => item is { Area: AdoptionArea.SpecKit, Action: AdoptionAction.Upgrade }) && !options.Flag("--skip-speckit"))
        {
            await UpgradeSpecKitAsync(manifest.Root, options.Value("--speckit-preset"), done, manual, cancellationToken);
        }

        if (plan.Items.Any(item => item is { Area: AdoptionArea.SpecKit, Action: AdoptionAction.Add }) && !options.Flag("--skip-speckit"))
        {
            await AddSpecKitPresetAsync(manifest.Root, options.Value("--speckit-preset"), done, manual, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Merges the <c>Edit</c> deny rules and, unless <paramref name="sandbox"/> is false, the sandbox write denials for the protected
    /// paths, the ones apply just added included, into the project's Claude Code settings. Settings that cannot be merged into are
    /// left as they are and the fix is listed.
    /// </summary>
    private static async Task AddProtectedPathSettingsAsync(string root, bool sandbox, List<string> done, List<string> manual, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        if (ClaudeSettingsMerger.Problem(existing) is string problem)
        {
            manual.Add($"{ClaudeSettingsMerger.RelativePath} {problem}, so no deny rules or sandbox write denials were added; fix it by hand and run {EngineCommand.Name} init");
            return;
        }

        IReadOnlyList<string> globs = DefaultProtectedPaths.For(ManifestLocator.Discover(root).Manifest!);
        IReadOnlyList<string> denyRules = PermissionDenyRules.For(root, globs);
        IReadOnlyList<string>? denyWrite = sandbox ? SandboxSettings.DenyWrite(root, globs) : null;
        (string json, bool changed) = ClaudeSettingsMerger.MergeProtectedPaths(existing, denyRules, denyWrite);
        if (!changed)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        done.Add($"Edit deny rules for {denyRules.Count} protected paths merged into permissions.deny in {ClaudeSettingsMerger.RelativePath}");
        if (denyWrite is not null)
        {
            done.Add($"sandbox write denials for {denyWrite.Count} protected paths merged into {ClaudeSettingsMerger.RelativePath} (in force while the Claude Code sandbox is on)");
        }
    }

    private async Task<ExitCode> UndoAsync(Manifest manifest, string? backup, bool force, CancellationToken cancellationToken)
    {
        if ((backup ?? AdoptionBackup.LatestApplied(manifest.Root)) is not string id || !AdoptionBackup.Exists(manifest.Root, id))
        {
            return await ReportAsync("no adoption backup of a finished apply found under .lockflight/backup/", ExitCode.Config);
        }

        if (!AdoptionBackup.WasApplied(manifest.Root, id))
        {
            return await ReportAsync(
                $"the apply that took backup {id} did not finish and was rolled back, so there is nothing to undo; pass --backup with the id of a finished apply", ExitCode.Config);
        }

        UndoResult result = AdoptionBackup.Undo(manifest.Root, id, force);
        await new LedgerWriter(manifest.Root, context.Time).AppendAsync(
            new LedgerEvent(LedgerEventType.ConfigWritten, "adopt:undo", $"backup {id}: {result.Restored} restored, {result.Removed} removed, {result.Kept.Count} kept", null), cancellationToken);
        await context.Output.WriteLineAsync($"Undid apply {id}: {result.Restored} files restored, {result.Removed} files apply added removed.");
        if (result.Kept.Count == 0)
        {
            return ExitCode.Pass;
        }

        await context.Output.WriteLineAsync($"Kept {result.Kept.Count} files that changed after apply (your later work), not reverted: {string.Join(", ", result.Kept.Take(MaxCustomisedListed))}{(result.Kept.Count > MaxCustomisedListed ? ", …" : string.Empty)}");
        await context.Output.WriteLineAsync("Revert them by hand, or run 'adopt undo --force' to overwrite them with the backup.");
        return ExitCode.Fail;
    }

    /// <summary>A Spec Kit newer than the pinned one keeps its files; only the Lockflight preset is installed into it.</summary>
    private static async Task AddSpecKitPresetAsync(string root, string? preset, List<string> done, List<string> manual, CancellationToken cancellationToken)
    {
        string installed = SpecKitUpgrade.InstalledVersion(root) ?? "?";
        if (preset is null)
        {
            manual.Add($"Spec Kit {installed} is newer than {SetupInventory.PinnedSpecKitVersion} and was kept as it is. Install the preset: uvx --from {SpecKitSources.PackageName}=={installed} specify preset add --dev <plugin>/speckit-preset (or pass --speckit-preset)");
            return;
        }

        (string? problem, string? source) = await SpecKitUpgrade.AddPresetOnlyAsync(root, Path.GetFullPath(preset), cancellationToken);
        if (problem is not null)
        {
            manual.Add($"Spec Kit {installed} was kept as it is, but {problem}");
            return;
        }

        done.Add($"Spec Kit {installed} kept as it is (newer than {SetupInventory.PinnedSpecKitVersion}, not downgraded); Lockflight preset installed{(source is null ? string.Empty : $" with Spec Kit {source}")}");
    }

    private static async Task UpgradeSpecKitAsync(string root, string? preset, List<string> done, List<string> manual, CancellationToken cancellationToken)
    {
        string installed = SpecKitUpgrade.InstalledVersion(root) ?? "?";
        (SpecKitMergeResult? result, string? problem, string? comparedWith, string? sources) = await SpecKitUpgrade.RunAsync(root, preset is null ? null : Path.GetFullPath(preset), cancellationToken);
        if (result is not null)
        {
            string basis = comparedWith is not null && comparedWith != installed ? $" (your {installed} compared with the stock {comparedWith})" : string.Empty;
            string origin = sources is null ? string.Empty : $"; stock {sources}";
            done.Add($"Spec Kit {SetupInventory.PinnedSpecKitVersion}: {result.Upgraded.Count} files upgraded, {result.Added.Count} added{basis}{origin}");
            string[] kept = [.. result.Customised.Except(result.ReplacedByPreset, StringComparer.Ordinal)];
            if (kept.Length > 0)
            {
                done.Add($"kept {kept.Length} customised Spec Kit files as they were: {string.Join(", ", kept.Take(MaxCustomisedListed))}{(kept.Length > MaxCustomisedListed ? ", …" : string.Empty)}");
            }

            if (result.Linked.Count > 0)
            {
                manual.Add($"left {result.Linked.Count} Spec Kit files behind symbolic links untouched (undo could not restore what they point to): {string.Join(", ", result.Linked.Take(MaxCustomisedListed))}{(result.Linked.Count > MaxCustomisedListed ? ", …" : string.Empty)}; upgrade them by hand");
            }

            foreach (string replaced in result.ReplacedByPreset)
            {
                manual.Add($"{replaced}: the Lockflight preset replaced your customised version (the preset owns this command). Yours is in the backup: `git show HEAD:{replaced}`; carry your changes over by hand if you need them");
            }
        }

        if (problem is not null)
        {
            manual.Add($"Spec Kit was left as it was: {problem}. Nothing in .specify/ changed; fix that and run adopt plan and adopt apply again. Do not run `specify init --force` over it: that overwrites your customised files");
        }

        if (preset is null)
        {
            manual.Add($"Spec Kit preset: uvx --from specify-cli=={SetupInventory.PinnedSpecKitVersion} specify preset add --dev <plugin>/speckit-preset (or pass --speckit-preset)");
        }
    }

    /// <summary>Appends entries to a manifest list, or records the lines to add by hand when the list cannot be extended in place.</summary>
    private static string Extend(string text, string key, IReadOnlyList<IReadOnlyList<string>> entries, List<string> done, List<string> manual, string summary)
    {
        if (entries.Count == 0)
        {
            return text;
        }

        if (ManifestListEditor.Append(text, key, entries) is string extended)
        {
            done.Add(summary);
            return extended;
        }

        manual.Add($"add under {key}: in {Manifest.FileName}: {string.Join("; ", entries.Select(entry => string.Join(" ", entry)))}");
        return text;
    }

    private static IReadOnlyList<string> UnitEntry(AdoptionItem item) =>
        item.Reason.Contains("kind: none", StringComparison.Ordinal) ? [$"path: {item.Subject}", "kind: none"] : [$"path: {item.Subject}"];

    private async Task<ExitCode> ReportAsync(string message, ExitCode exitCode)
    {
        await context.Error.WriteLineAsync($"lockflight: {message}");
        return exitCode;
    }
}
