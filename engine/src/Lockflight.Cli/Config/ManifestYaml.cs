namespace Lockflight.Cli.Config;

/// <summary>
/// The version 2 sections as <c>init</c> and the version 1 migration write them: every routing threshold spelled out from a
/// preset, so the manifest holds the numbers that judge, and the approvals with the check mode given.
/// </summary>
internal static class ManifestYaml
{
    /// <summary>The <c>routing:</c> section of <paramref name="preset"/> with every value written down.</summary>
    public static string Routing(RoutingPreset preset)
    {
        RoutingSettings values = RoutingPresets.For(preset);
        RoutingRequirements require = values.Require;
        RoutingCoverage coverage = values.Coverage;
        return $"""
            routing:                        # how a pull request's change class is computed and what each class must prove
              preset: {RoutingPresetNames.ToName(preset)}
              floor: {ChangeClassNames.ToName(values.Floor)}
              fix: {"{"} maxLines: {values.FixMaxLines}, maxFiles: {values.FixMaxFiles} {"}"}
              largeLines: {values.LargeLines}
              structural: {ChangeClassNames.ToName(values.Structural)}
              critical: []                  # globs of critical paths, such as "src/Payments/**"
              coverage: {"{"} fix: {coverage.Fix}, behaviour: {coverage.Behaviour}, feature: {coverage.Feature}, critical: {coverage.Critical} {"}"}
              require:
                specFrom: {ChangeClassNames.ToName(require.SpecFrom)}
                reviewFrom: {ChangeClassNames.ToName(require.ReviewFrom)}
                reviewKeyFrom: {ChangeClassNames.ToName(require.ReviewKeyFrom)}
                architectureFrom: {ChangeClassNames.ToName(require.ArchitectureFrom)}
                architectureDeclaredFrom: {ChangeClassNames.ToName(require.ArchitectureDeclaredFrom)}

            """;
    }

    /// <summary>The <c>approvals:</c> section with <paramref name="check"/> and the preset's <c>specFirstFrom</c>.</summary>
    public static string Approvals(ApprovalCheckMode check, RoutingPreset preset) => $"""
        approvals:                      # the approval of record is the platform's review of the pull request
          check: {(check == ApprovalCheckMode.Fail ? "fail" : "report")}
          default: same-pr
          specFirstFrom: {ChangeClassNames.ToName(RoutingPresets.SpecFirstFrom(preset))}
          specFirstPaths: []
          specPaths: ["specs/**"]

        """;
}
