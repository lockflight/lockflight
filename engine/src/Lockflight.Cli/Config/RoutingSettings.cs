namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest's <c>routing:</c> section: how a pull request's change class is computed from its diff and what each class must
/// prove. Keys the manifest leaves out take the preset's values and are named in <see cref="FilledFromPreset"/>, so
/// <c>doctor</c> can say which numbers are not written down. Read and validated; the classifier that uses it comes later.
/// </summary>
/// <param name="Floor">The lowest class any pull request is judged at.</param>
/// <param name="FixMaxLines">A change larger than this many counted lines is at least <c>behaviour</c>.</param>
/// <param name="FixMaxFiles">A change to more than this many counted files is at least <c>behaviour</c>.</param>
/// <param name="LargeLines">A change larger than this many counted lines is at least <c>feature</c>.</param>
/// <param name="Structural">The class of a new project, a build-wide file or a solution change: feature or critical.</param>
internal sealed record RoutingSettings(
    RoutingPreset Preset,
    ChangeClass Floor,
    int FixMaxLines,
    int FixMaxFiles,
    int LargeLines,
    ChangeClass Structural,
    RoutingCoverage Coverage,
    RoutingRequirements Require)
{
    public const int MaxFixLines = 10_000;
    public const int MaxFixFiles = 1_000;

    /// <summary>Globs of critical paths (<c>routing.critical</c>), matched on both sides of every rename.</summary>
    public IReadOnlyList<string> Critical { get; init; } = [];

    /// <summary>Extra globs that mean "database", besides migrations and SQL files (<c>routing.dbPaths</c>).</summary>
    public IReadOnlyList<string> DbPaths { get; init; } = [];

    /// <summary>Globs of the team's generated code, left out of size and shape (<c>routing.generatedPaths</c>).</summary>
    public IReadOnlyList<string> GeneratedPaths { get; init; } = [];

    /// <summary>Globs of vendored code, samples and tools, left out of size and shape (<c>routing.nonProduction</c>).</summary>
    public IReadOnlyList<string> NonProduction { get; init; } = [];

    /// <summary>CI files a platform ruleset requires: changing one is critical (<c>routing.requiredWorkflows</c>).</summary>
    public IReadOnlyList<string> RequiredWorkflows { get; init; } = [];

    /// <summary>Globs of project directories whose public surface is an API (<c>routing.apiProjects</c>).</summary>
    public IReadOnlyList<string> ApiProjects { get; init; } = [];

    /// <summary>The architecture layers, inner first (<c>routing.layers</c>); empty when none are declared.</summary>
    public IReadOnlyList<RoutingLayer> Layers { get; init; } = [];

    /// <summary>The keys (<c>fix.maxLines</c>, <c>require.reviewFrom</c>, …) the manifest left out and the preset filled in.</summary>
    public IReadOnlyList<string> FilledFromPreset { get; init; } = [];

    /// <summary>Whether the manifest has a <c>routing:</c> section; without one every value is the default preset's.</summary>
    public bool FromManifest { get; init; }

    public static RoutingSettings Default { get; } = RoutingPresets.For(RoutingPreset.Default) with { FilledFromPreset = RoutingPresets.Keys };
}
