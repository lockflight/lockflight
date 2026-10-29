using Lockflight.Cli.Config;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// What <c>doctor</c> says about a version 2 manifest's newer sections: routing values the preset filled in (a manifest should
/// spell out the numbers that judge it), critical globs that make every change critical, and the reserved <c>autonomy</c>.
/// </summary>
internal static class ManifestSettingsChecks
{
    private const string RoutingCheck = "routing";
    private const string AutonomyCheck = "autonomy";
    private const int MaxListed = 5;

    private static readonly string[] MatchEverything = ["**", "**/*"];

    public static IReadOnlyList<DoctorCheck> Check(string root)
    {
        if (ManifestLocator.Discover(root).Manifest is not { Version: ManifestVersions.Current } manifest)
        {
            return [];
        }

        List<DoctorCheck> checks = [Routing(manifest.Routing)];
        if (manifest.Autonomy is not null)
        {
            checks.Add(new(AutonomyCheck, CheckStatus.Pass, "reserved, has no effect in this version", null));
        }

        return checks;
    }

    private static DoctorCheck Routing(RoutingSettings routing)
    {
        string preset = RoutingPresetNames.ToName(routing.Preset);
        if (!routing.FromManifest)
        {
            return new(
                RoutingCheck, CheckStatus.Warn, $"no routing section: preset {preset} with no critical paths",
                "add routing: with preset: and its values spelled out (a human edit; the manifest is protected)");
        }

        if (routing.Critical.Concat(routing.ApiProjects).FirstOrDefault(glob => MatchEverything.Contains(glob, StringComparer.Ordinal)) is string everything)
        {
            return new(
                RoutingCheck, CheckStatus.Warn, $"'{everything}' in routing.critical or routing.apiProjects matches every path, so every change is critical",
                "list the critical directories instead, unless every change should be critical");
        }

        if (routing.FilledFromPreset.Count > 0)
        {
            string listed = string.Join(", ", routing.FilledFromPreset.Take(MaxListed));
            string more = routing.FilledFromPreset.Count > MaxListed ? $" and {routing.FilledFromPreset.Count - MaxListed} more" : string.Empty;
            return new(
                RoutingCheck, CheckStatus.Warn, $"preset {preset}; {routing.FilledFromPreset.Count} value(s) not written down, taken from the preset: {listed}{more}",
                "write them out under routing: so the numbers that judge a change are in the manifest (a later engine may change a preset's values)");
        }

        return new(RoutingCheck, CheckStatus.Pass, $"preset {preset}, every value written down", null);
    }
}
