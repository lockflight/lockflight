namespace Lockflight.Cli.Config;

/// <summary>
/// Patch coverage: the share of changed, coverable lines that tests execute. Below <see cref="MinChangedLines"/> coverable changed
/// lines the gate does not judge (a percentage of three lines says nothing).
/// </summary>
internal sealed record CoverageSettings(CoverageCollector Collector, int PatchThreshold, int MinChangedLines)
{
    public const int DefaultPatchThreshold = 80;
    public const int DefaultMinChangedLines = 10;

    public static CoverageSettings Default { get; } = new(CoverageCollector.None, DefaultPatchThreshold, DefaultMinChangedLines);
}
