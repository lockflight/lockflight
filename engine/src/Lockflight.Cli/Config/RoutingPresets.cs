namespace Lockflight.Cli.Config;

/// <summary>
/// The values each <see cref="RoutingPreset"/> seeds. <c>init --preset</c> writes them out, so a manifest holds the numbers that
/// judge; the reader uses them only for keys a hand-written manifest leaves out.
/// </summary>
internal static class RoutingPresets
{
    /// <summary>Every <c>routing</c> key a preset gives a value, in the order <c>init</c> writes them.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        "floor", "fix.maxLines", "fix.maxFiles", "largeLines", "structural",
        "coverage.fix", "coverage.behaviour", "coverage.feature", "coverage.critical",
        "require.specFrom", "require.reviewFrom", "require.reviewKeyFrom", "require.architectureFrom", "require.architectureDeclaredFrom",
    ];

    public static RoutingSettings For(RoutingPreset preset) => preset switch
    {
        RoutingPreset.Relaxed => new(
            preset, ChangeClass.Fix, FixMaxLines: 40, FixMaxFiles: 3, LargeLines: 800, ChangeClass.Critical,
            new RoutingCoverage(70, 70, 70, 80),
            new RoutingRequirements(ChangeClass.Critical, ChangeClass.Feature, ChangeClass.Critical, ChangeClass.Critical, ArchitectureDeclaredFrom: null)),
        RoutingPreset.Default => new(
            preset, ChangeClass.Fix, FixMaxLines: 20, FixMaxFiles: 2, LargeLines: 400, ChangeClass.Critical,
            new RoutingCoverage(80, 80, 80, 90),
            new RoutingRequirements(ChangeClass.Feature, ChangeClass.Behaviour, ChangeClass.Critical, ChangeClass.Feature, ArchitectureDeclaredFrom: null)),
        RoutingPreset.Strict => new(
            preset, ChangeClass.Behaviour, FixMaxLines: 10, FixMaxFiles: 1, LargeLines: 200, ChangeClass.Critical,
            new RoutingCoverage(85, 85, 90, 95),
            new RoutingRequirements(ChangeClass.Behaviour, ChangeClass.Behaviour, ChangeClass.Feature, ChangeClass.Behaviour, ChangeClass.Critical)),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown routing preset."),
    };

    /// <summary>The <c>approvals.specFirstFrom</c> a preset seeds: <c>critical</c> for strict, none otherwise.</summary>
    public static ChangeClass? SpecFirstFrom(RoutingPreset preset) => preset == RoutingPreset.Strict ? ChangeClass.Critical : null;
}
