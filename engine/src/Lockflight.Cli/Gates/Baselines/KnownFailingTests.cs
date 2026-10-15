namespace Lockflight.Cli.Gates.Baselines;

/// <summary>
/// Tests a brownfield project already fails when Lockflight is adopted (<c>.lockflight/baselines/known-failing-tests.json</c>,
/// protected). The test gate fails only on failures outside this list. Written by <c>lockflight baseline tests</c> (a human).
/// </summary>
internal sealed class KnownFailingTests(string projectRoot, string? unitPath)
{
    public const string FileName = "known-failing-tests.json";

    private const string Schema = "lockflight.baseline.tests/v1";

    /// <summary>The file relative to the project root: under the unit's folder for a unit of a workspace.</summary>
    public string RelativePath => BaselinePaths.Relative(unitPath, FileName);

    private BaselineList List => new(BaselinePaths.Full(projectRoot, unitPath, FileName), Schema, "tests");

    public IReadOnlySet<string> Load() => List.Load();

    /// <summary>The known-failing tests of the root and of every unit of a workspace together (red/green spans units).</summary>
    public static IReadOnlySet<string> LoadAll(Config.Manifest manifest) =>
        manifest.Units.Select(unit => unit.Path).Prepend(null).SelectMany(unit => new KnownFailingTests(manifest.Root, unit).Load()).ToHashSet(StringComparer.Ordinal);

    public void Save(IEnumerable<string> failedTests) => List.Save(failedTests);
}
