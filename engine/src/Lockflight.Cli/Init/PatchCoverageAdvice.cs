using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Init;

/// <summary>
/// What a project that runs the <c>pr</c> gates in CI without <c>gates.coverage.collector</c> is told: its <c>coverage.patch</c>
/// step reports N/A there (the reason is only in the CI log), and the collector to set with the package its test runner needs.
/// <c>init</c> prints it and <c>doctor</c> warns with it; neither writes the setting, which is the team's choice.
/// </summary>
internal static class PatchCoverageAdvice
{
    public const string GuideUrl = "https://github.com/lockflight/lockflight/blob/main/docs/guides/configuration.md#patch-coverage-needs-a-collector";

    // coverlet (MIT) is the default for new setups.
    private const CoverageCollector Suggested = CoverageCollector.Coverlet;
    private const string SuggestedName = "coverlet";

    // The GitHub judge action, the gates command (Azure, GitLab, older workflows) and the judge command (Bitbucket, unit pointers).
    private static readonly string[] PrGatesMarkers = ["mode: judge", "--profile pr", "lockflight ci "];

    /// <summary>
    /// Whether the advice applies to a project with this manifest: no collector, and not a unit of a workspace shell, whose CI
    /// gates take the shell's collector when the unit sets none (with a shell pointer).
    /// </summary>
    public static bool AppliesTo(Manifest manifest, string root) =>
        manifest.Gates.Coverage.Collector == CoverageCollector.None
        && manifest.Shell is null;

    /// <summary>What to do: the setting, and the package for the test runner the project's <c>global.json</c> picks.</summary>
    public static string Fix(string root)
    {
        string package = CoverageStep.PackageOf(Suggested, TestRunnerDetection.For(root))!;
        return $"set gates.coverage.collector: {SuggestedName} in {Manifest.FileName} (a person's edit: it is protected) and add {package} to every test project (dotnet add <test project> package {package})";
    }

    /// <summary>
    /// The CI file <c>init</c> writes that runs the <c>pr</c> gates, relative to the root; null when there is none. The Bitbucket
    /// copy to merge by hand is left out: it runs only once merged into the pipeline.
    /// </summary>
    public static string? PrGatesFile(string root) => CiStepMigration.KnownFiles
        .Where(relativePath => relativePath != BitbucketPipelinesTemplate.FragmentPath)
        .FirstOrDefault(relativePath => ReadOrEmpty(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar))) is string text
            && PrGatesMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)));

    /// <summary>The line <c>init</c> prints after writing a CI file for a project without a collector.</summary>
    public static string InitLine(string root) =>
        $"Patch coverage: gates.coverage.collector is not set, so the pr profile's coverage.patch step reports N/A in CI (not judged). To judge it, {Fix(root)}: {GuideUrl}";

    // A file doctor cannot read counts as not running the gates: doctor reports, it does not crash.
    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
