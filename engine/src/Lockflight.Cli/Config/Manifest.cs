namespace Lockflight.Cli.Config;

/// <summary>
/// A loaded <c>lockflight.yaml</c>: the project root it was found in and the rules it declares. <see cref="Units"/> is empty for a
/// single repository, which is then the one implicit unit at the root.
/// </summary>
internal sealed record Manifest(string Root, IReadOnlyList<string> ProtectedGlobs, bool StopGate, string? StopTarget, GateSettings Gates)
{
    public const string FileName = "lockflight.yaml";

    /// <summary>The policy <c>workspace sync</c> vendors into a unit repository for its CI.</summary>
    public const string UnitFileName = "lockflight.unit.yaml";

    public IReadOnlyList<Unit> Units { get; init; } = [];

    /// <summary>What a review needs to be the review of record (<c>review:</c>); the defaults when the manifest has no such section.</summary>
    public ReviewSettings Review { get; init; } = ReviewSettings.Default;

    /// <summary>The manifest's <c>version</c>: 1 (or absent, read with migration defaults) or 2.</summary>
    public int Version { get; init; } = ManifestVersions.Current;

    /// <summary>The lowest engine version the manifest needs (<c>requiresEngine</c>); null when it names none.</summary>
    public string? RequiresEngine { get; init; }

    /// <summary>How a change's class is computed and what each class must prove (<c>routing:</c>).</summary>
    public RoutingSettings Routing { get; init; } = RoutingSettings.Default;

    /// <summary>Where specs are approved and what the CI judge checks about it (<c>approvals:</c>).</summary>
    public ApprovalSettings Approvals { get; init; } = ApprovalSettings.Default;

    /// <summary>The reserved <c>autonomy:</c> section; null when the manifest has none. Nothing but <c>doctor</c> reads it.</summary>
    public AutonomySettings? Autonomy { get; init; }

    /// <summary>The workspace shell this repository is a unit of (<c>shell:</c>); null for a single repository, a shell or a monorepo.</summary>
    public ShellSettings? Shell { get; init; }

    /// <summary>The CI judge's limits and path lists (<c>ci:</c>); the defaults when the manifest has no such section.</summary>
    public CiSettings Ci { get; init; } = CiSettings.Default;
}
