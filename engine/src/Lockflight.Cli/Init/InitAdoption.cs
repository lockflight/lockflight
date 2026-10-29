using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Init;

/// <summary>
/// What <c>init</c> takes over from an existing setup (the <see cref="SetupInventory"/> plan): the write-guard paths of the team's
/// hooks into <c>protected:</c>, units the manifest does not list, the <c>shell:</c> pointer of <c>--unit-of</c>, and, with a
/// preset, an upgrade of the team's Spec Kit. Keeps and flags; never replaces the team's own files. Git is the undo: init's
/// changes are committed as one commit and reverted with <c>git revert</c>.
/// </summary>
internal static class InitAdoption
{
    /// <summary>Stands in for the preset's directory when it was not found on this machine.</summary>
    public const string PresetPlaceholder = "<plugin>/speckit-preset";

    private const int MaxListed = 8;

    /// <summary>
    /// The manifest init writes: the template for a new project (with the units it finds, unless it is a unit of a shell), else the
    /// project's own text; then the hooks' guarded paths, units no entry lists and the shell pointer, each only when missing.
    /// </summary>
    public static ManifestPlan Manifest(string? existingText, Manifest? existing, InitProfile profile, AdoptionPlan plan, IReadOnlyList<UnitCandidate> discovered, ShellSettings? shell)
    {
        List<string> manual = [];
        // A shell pointer needs a version 2 manifest; a new one is written as version 2.
        string text = existingText ?? (shell is null ? Templates.Manifest(profile) : Templates.Manifest(profile).Replace("version: 1", "version: 2", StringComparison.Ordinal));
        if (shell is null && existingText is null)
        {
            text = Extend(text, "units", [.. discovered.Select(unit => unit.ManifestEntry)], manual);
        }
        else if (shell is null)
        {
            // A unit changes what the gates build, and the agent can make a directory look like one: an existing manifest's units
            // are a person's edit.
            manual.AddRange(plan.Items.Where(item => item is { Area: AdoptionArea.Unit, Action: AdoptionAction.Add })
                .Select(item => $"add under units: in {Config.Manifest.FileName}: {string.Join(" ", UnitEntry(item))} ({item.Reason})"));
        }

        IReadOnlyList<string> listed = existing?.ProtectedGlobs ?? [];
        string[] globs = [.. plan.Items.Where(item => item.Action == AdoptionAction.Protect).SelectMany(item => item.Globs)
            .Distinct(StringComparer.Ordinal).Where(glob => !listed.Contains(glob, StringComparer.Ordinal))];
        text = Extend(text, "protected", [.. globs.Select(glob => (IReadOnlyList<string>)[$"\"{glob}\""])], manual);
        if (shell is not null && existing?.Shell is null)
        {
            string separator = text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n";
            text += $"{separator}\n# This repository is a unit of a workspace shell (written by lockflight init --unit-of).\nshell:\n  url: \"{shell.Url}\"\n  unit: {shell.Unit}\n";
        }

        if (shell is not null && existing?.Shell is ShellSettings current && current.Url == shell.Url && current.Unit != shell.Unit)
        {
            manual.Add($"shell.unit in {Config.Manifest.FileName} is {current.Unit}, not {shell.Unit}; change it by hand");
        }

        return new ManifestPlan(text, text != existingText, manual);
    }

    /// <summary>Why this repository cannot become a unit of <paramref name="shell"/>; null when it can (or no shell was asked for).</summary>
    public static string? ShellProblem(Manifest? existing, ShellSettings? shell) => (existing, shell) switch
    {
        (_, null) => null,
        (null, _) => null,
        ({ Shell: null, Version: < ManifestVersions.Current }, _) => $"shell: needs a version {ManifestVersions.Current} manifest, and {Config.Manifest.FileName} is version {ManifestVersions.Legacy}; set 'version: {ManifestVersions.Current}' first (or let init write a new one)",
        ({ Units.Count: > 0 }, _) => $"{Config.Manifest.FileName} lists units:, so this repository is a shell or a monorepo; a unit of a shell has none",
        ({ Shell: ShellSettings current }, _) when current.Url != shell.Url => $"{Config.Manifest.FileName} already names the shell {current.Url}; change shell: by hand",
        _ => null,
    };

    /// <summary>What the plan keeps with a warning, for the person to read: one line per flagged item.</summary>
    public static IEnumerable<string> Notes(AdoptionPlan plan) =>
        plan.Items.Where(item => item.Action == AdoptionAction.Flag).Select(item => $"note       {item.Subject}: {item.Reason}");

    /// <summary>The existing features init marks as predating Lockflight; only on the init that creates the manifest.</summary>
    public static IReadOnlyList<string> PredatingFeatures(AdoptionPlan plan) =>
        [.. plan.Items.Where(item => item is { Area: AdoptionArea.Feature, Action: AdoptionAction.Mark }).Select(item => item.Subject)];

    /// <summary>Whether the plan upgrades the team's Spec Kit or adds the Lockflight preset to it.</summary>
    public static bool ChangesSpecKit(AdoptionPlan plan) =>
        plan.Items.Any(item => item is { Area: AdoptionArea.SpecKit, Action: AdoptionAction.Upgrade or AdoptionAction.Add });

    /// <summary>
    /// Upgrades the team's Spec Kit to the pinned version with the preset, or, for a newer Spec Kit, installs only the preset. Lines
    /// for what was done go to <paramref name="done"/>, what needs the person to <paramref name="manual"/>.
    /// </summary>
    public static async Task AdoptSpecKitAsync(string root, AdoptionPlan plan, string preset, List<string> done, List<string> manual, CancellationToken cancellationToken)
    {
        string installed = SpecKitUpgrade.InstalledVersion(root) ?? "?";
        if (plan.Items.Any(item => item is { Area: AdoptionArea.SpecKit, Action: AdoptionAction.Add }))
        {
            (string? presetProblem, string? source) = await SpecKitUpgrade.AddPresetOnlyAsync(root, Path.GetFullPath(preset), cancellationToken);
            if (presetProblem is not null)
            {
                manual.Add($"Spec Kit {installed} was kept as it is, but {presetProblem}");
                return;
            }

            done.Add($"Spec Kit {installed} kept as it is (newer than {SetupInventory.PinnedSpecKitVersion}, not downgraded); Lockflight preset installed{(source is null ? string.Empty : $" with Spec Kit {source}")}");
            return;
        }

        (SpecKitMergeResult? result, string? problem, string? comparedWith, string? sources) = await SpecKitUpgrade.RunAsync(root, Path.GetFullPath(preset), cancellationToken);
        if (result is not null)
        {
            string basis = comparedWith is not null && comparedWith != installed ? $" (your {installed} compared with the stock {comparedWith})" : string.Empty;
            string origin = sources is null ? string.Empty : $"; stock {sources}";
            done.Add($"Spec Kit {SetupInventory.PinnedSpecKitVersion}: {result.Upgraded.Count} files upgraded, {result.Added.Count} added{basis}{origin}");
            string[] kept = [.. result.Customised.Except(result.ReplacedByPreset, StringComparer.Ordinal)];
            if (kept.Length > 0)
            {
                done.Add($"kept {kept.Length} customised Spec Kit files as they were: {Listed(kept)}");
            }

            if (result.Linked.Count > 0)
            {
                manual.Add($"left {result.Linked.Count} Spec Kit files behind symbolic links untouched: {Listed(result.Linked)}; upgrade them by hand");
            }

            foreach (string replaced in result.ReplacedByPreset)
            {
                manual.Add($"{replaced}: the Lockflight preset replaced your customised version (the preset owns this command); yours is in git (`git show HEAD:{replaced}`), carry your changes over by hand if you need them");
            }
        }

        if (problem is not null)
        {
            manual.Add($"Spec Kit was left as it was: {problem}. Fix that and run init again with --speckit-preset. Do not run `specify init --force` over it: that overwrites your customised files");
        }
    }

    /// <summary>The line that says how to upgrade the team's Spec Kit, when init left it as it is.</summary>
    public static string SpecKitLine(string root, string? presetDirectory) =>
        $"Spec Kit was left as it is. To upgrade it to {SetupInventory.PinnedSpecKitVersion} with the Lockflight preset (customised files are kept and listed), run "
        + $"`{EngineCommand.Linked} init --speckit-preset {(presetDirectory is null ? PresetPlaceholder : ShellQuoting.Quote(presetDirectory))}` in {ShellQuoting.Quote(root)}"
        + (presetDirectory is null ? $", with {PresetPlaceholder} the speckit-preset directory of the installed Lockflight plugin (under ~/.claude/plugins/cache/lockflight/lockflight/<version>/) or of a Lockflight checkout (plugins/lockflight/speckit-preset)." : ".");

    private static string Extend(string text, string key, IReadOnlyList<IReadOnlyList<string>> entries, List<string> manual)
    {
        if (entries.Count == 0)
        {
            return text;
        }

        if (ManifestListEditor.Append(text, key, entries) is string extended)
        {
            return extended;
        }

        manual.Add($"add under {key}: in {Config.Manifest.FileName}: {string.Join("; ", entries.Select(entry => string.Join(" ", entry)))}");
        return text;
    }

    private static IReadOnlyList<string> UnitEntry(AdoptionItem item) =>
        item.Reason.Contains("kind: none", StringComparison.Ordinal) ? [$"path: {item.Subject}", "kind: none"] : [$"path: {item.Subject}"];

    private static string Listed(IReadOnlyList<string> files) =>
        $"{string.Join(", ", files.Take(MaxListed))}{(files.Count > MaxListed ? ", …" : string.Empty)}";
}
