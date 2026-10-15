namespace Lockflight.Cli.Config;

/// <summary>
/// Per-repository gate configuration from the manifest's <c>gates:</c> section: which target framework tests run under (a repo that
/// multi-targets may not have every runtime installed) and the wall-clock budget for one gate run.
/// </summary>
internal sealed record GateSettings(string? TestFramework, int TimeoutSeconds, CoverageSettings Coverage)
{
    /// <summary>The solution or project file the gates build and test (<c>gates.build</c>); null for the one in the target directory.</summary>
    public string? BuildFile { get; init; }

    public const int DefaultTimeoutSeconds = 600;
    public const int MaxTimeoutSeconds = 7200;

    public static GateSettings Default { get; } = new(TestFramework: null, DefaultTimeoutSeconds, CoverageSettings.Default);
}
