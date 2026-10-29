namespace Lockflight.Cli.Config;

/// <summary>
/// Per-repository gate configuration from the manifest's <c>gates:</c> section: which target framework tests run under (a repo that
/// multi-targets may not have every runtime installed) and the wall-clock budget for one gate run.
/// </summary>
internal sealed record GateSettings(string? TestFramework, int TimeoutSeconds, CoverageSettings Coverage)
{
    /// <summary>The solution or project file the gates build and test (<c>gates.build</c>); null for the one in the target directory.</summary>
    public string? BuildFile { get; init; }

    /// <summary>
    /// How the pr profile's <c>quality.suppressions</c> step judges (<c>gates.quality.suppressions</c>). Absent, it reports only, so a
    /// new engine never turns a green repository red by itself; a team sets <c>fail</c> to let it fail the run.
    /// </summary>
    public GateMode Suppressions { get; init; } = GateMode.Report;

    /// <summary>How the pr profile's <c>docs.references</c> step judges (<c>gates.docs.references</c>); absent, it reports only.</summary>
    public GateMode DocsReferences { get; init; } = GateMode.Report;

    /// <summary>
    /// How the pr profile's <c>ef.pendingModelChanges</c> step judges (<c>gates.ef.pendingModelChanges</c>); absent, <c>auto</c>: it
    /// runs and can fail only when the repository pins <c>dotnet-ef</c> in a tool manifest.
    /// </summary>
    public GateMode EfPendingModelChanges { get; init; } = GateMode.Auto;

    /// <summary>
    /// <c>gates.test.stopFilter</c>: tests the stop profile and red/green's whole-suite regression passes leave out (the pr profile
    /// and CI run them); a unit's own <c>test.stopFilter</c> replaces it. Null for none.
    /// </summary>
    public string? StopFilter { get; init; }

    public const int DefaultTimeoutSeconds = 600;
    public const int MaxTimeoutSeconds = 7200;

    public static GateSettings Default { get; } = new(TestFramework: null, DefaultTimeoutSeconds, CoverageSettings.Default);
}
