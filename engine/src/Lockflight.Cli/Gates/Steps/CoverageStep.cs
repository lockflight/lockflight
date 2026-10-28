using System.Diagnostics;
using System.Globalization;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Gates.Steps;

/// <summary>
/// Patch coverage (pr profile): of the changed lines that are coverable, the share the tests executed must reach the threshold.
/// Reads the Cobertura reports the test run just wrote; judges nothing when too few coverable lines changed.
/// </summary>
internal sealed class CoverageStep : IGateStep
{
    public const string StepName = "coverage.patch";

    private const int MaxUncoveredListed = 5;

    private const int MaxLinesListed = 8;

    public string Name => StepName;

    public StepScope Scope => StepScope.Unit;

    public string CacheKey => StepName;

    /// <summary>
    /// Test arguments that make the configured collector write Cobertura; empty when coverage is not configured. On VSTest the
    /// collectors are data collectors (<c>--collect</c>): coverlet.collector, or Microsoft's collector that Microsoft.NET.Test.Sdk brings.
    /// </summary>
    public static IReadOnlyList<string> TestArguments(CoverageCollector collector, TestRunnerKind runner) => (collector, runner) switch
    {
        (CoverageCollector.Microsoft, TestRunnerKind.MicrosoftTestingPlatform) => ["--coverage", "--coverage-output-format", "cobertura"],
        (CoverageCollector.Coverlet, TestRunnerKind.MicrosoftTestingPlatform) => ["--coverlet", "--coverlet-output-format", "cobertura"],
        (CoverageCollector.Microsoft, TestRunnerKind.VSTest) => ["--collect", "Code Coverage;Format=cobertura"],
        (CoverageCollector.Coverlet, TestRunnerKind.VSTest) => ["--collect", "XPlat Code Coverage"],
        _ => [],
    };

    /// <summary>The NuGet package every test project must reference for the collector to run; null for none.</summary>
    public static string? PackageOf(CoverageCollector collector, TestRunnerKind runner) => (collector, runner) switch
    {
        (CoverageCollector.Microsoft, TestRunnerKind.MicrosoftTestingPlatform) => "Microsoft.Testing.Extensions.CodeCoverage",
        (CoverageCollector.Coverlet, TestRunnerKind.MicrosoftTestingPlatform) => "coverlet.MTP",
        (CoverageCollector.Microsoft, TestRunnerKind.VSTest) => "Microsoft.NET.Test.Sdk",
        (CoverageCollector.Coverlet, TestRunnerKind.VSTest) => "coverlet.collector",
        _ => null,
    };

    /// <summary>What to do when the test applications reject the collector's options (exit 5, invalid command line).</summary>
    public static string MissingExtension(CoverageCollector collector, TestRunnerKind runner) =>
        $"a test application rejected the coverage options (exit 5, invalid command line): lockflight.yaml sets gates.coverage.collector but not every test project references {PackageOf(collector, runner)}. " +
        $"Add that package to each test project (dotnet add <test project> package {PackageOf(collector, runner)}), or remove gates.coverage.collector; 'lockflight doctor' lists the projects.";

    /// <summary>Test code by convention: a path segment ending in Tests/Test/Specs, or a tests/test folder.</summary>
    public static bool IsTestCode(string relativeFile) =>
        relativeFile.Split('/').SkipLast(1).Any(segment =>
            segment is "tests" or "test" or "Tests" or "Test"
            || segment.EndsWith("Tests", StringComparison.Ordinal) || segment.EndsWith(".Test", StringComparison.Ordinal) || segment.EndsWith(".Specs", StringComparison.Ordinal));

    /// <summary>The directory of the nearest .csproj above a file (relative, '/'-separated), or the file's own directory.</summary>
    private static string ProjectDirectoryOf(string root, string relativeFile)
    {
        string? directory = Path.GetDirectoryName(relativeFile.Replace('/', Path.DirectorySeparatorChar));
        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.Exists(Path.Combine(root, directory)) && Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj").Any())
            {
                return directory.Replace('\\', '/');
            }

            directory = Path.GetDirectoryName(directory);
        }

        return Path.GetDirectoryName(relativeFile.Replace('/', Path.DirectorySeparatorChar))?.Replace('\\', '/') ?? string.Empty;
    }

    /// <summary>
    /// The repository the target was built in: the nearest directory at or above it with a <c>.git</c> folder or file. A
    /// deterministic build maps that directory (the source control root) to <c>/_/</c>. Null outside a repository.
    /// </summary>
    public static string? RepositoryRootOf(string target)
    {
        for (string? directory = Path.GetFullPath(target); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")))
            {
                return directory;
            }
        }

        return null;
    }

    /// <param name="facts">When the tests started and, on VSTest, where they wrote their results (Cobertura in per-run GUID folders).</param>
    public async Task<GateStepResult> RunAsync(GateRunContext run, GateRunFacts facts, CancellationToken cancellationToken)
    {
        DateTimeOffset testsStarted = facts.TestsStarted ?? throw new InvalidOperationException("the coverage step reads the test run: it runs after the test step");
        Stopwatch stopwatch = Stopwatch.StartNew();
        GateStep step = await JudgeAsync(run, testsStarted, facts.TestResultsDirectory, cancellationToken);
        return new(step with { DurationMs = stopwatch.ElapsedMilliseconds }, facts);
    }

    private static async Task<GateStep> JudgeAsync(GateRunContext run, DateTimeOffset testsStarted, string? resultsDirectory, CancellationToken cancellationToken)
    {
        CoverageSettings settings = run.Settings.Coverage;
        if (settings.Collector == CoverageCollector.None)
        {
            return new GateStep(StepName, 0, Verdict.NotApplicable, 0, "not configured, so not judged (set gates.coverage.collector in lockflight.yaml)");
        }

        IEnumerable<string> searched = resultsDirectory is not null && Directory.Exists(resultsDirectory)
            ? Directory.EnumerateFiles(resultsDirectory, "*.cobertura.xml", SearchOption.AllDirectories)
            : Directory.EnumerateFiles(run.Target, "*.cobertura.xml", SearchOption.AllDirectories);
        string[] reports = [.. searched.Where(path => File.GetLastWriteTimeUtc(path) >= testsStarted.UtcDateTime.AddSeconds(-1))];
        if (reports.Length == 0)
        {
            return new GateStep(StepName, 0, Verdict.NotEarned, 0, "the test run wrote no Cobertura report; does every test project reference the collector package?");
        }

        LineCoverage coverage = new();
        string? sourceRoot = RepositoryRootOf(run.Target);
        foreach (string report in reports)
        {
            coverage.AddCobertura(report, run.ProjectRoot, sourceRoot);
        }

        if (await ChangedLines.FindAsync(run.ProjectRoot, run.BaseRef, cancellationToken) is not IReadOnlyDictionary<string, IReadOnlySet<int>> allChanged)
        {
            return new GateStep(StepName, 0, Verdict.Error, 0, "git could not list the changed lines; with --base, is the checkout deep enough (fetch-depth: 0), and does every unit repository have that ref?");
        }

        // A unit's run judges its own files: another unit's changes are that unit's run's to judge.
        IReadOnlyDictionary<string, IReadOnlySet<int>> changed = run.UnitPath is string unit
            ? allChanged.Where(entry => entry.Key.StartsWith(unit + "/", StringComparison.OrdinalIgnoreCase)).ToDictionary(StringComparer.Ordinal)
            : allChanged;

        // A changed production file in a project no report instruments (a new project without tests, a collector exclusion) is not
        // "not coverable": all its changed lines count as uncovered. Files of instrumented projects that are absent from the reports
        // simply have no coverable lines (constants, interfaces). Test code itself is never instrumented and is left out.
        string[] uninstrumented = [.. changed.Keys
            .Where(file => !coverage.Covers(file) && !IsTestCode(file) && !coverage.CoversAnyUnder(ProjectDirectoryOf(run.ProjectRoot, file)))
            .Order(StringComparer.Ordinal)];
        List<(string File, int Line, int Hits)> coverable = [.. changed
            .SelectMany(entry => entry.Value.Select(line => (File: entry.Key, Line: line, Hits: uninstrumented.Contains(entry.Key) ? 0 : coverage.HitsAt(entry.Key, line))))
            .Where(entry => entry.Hits is not null)
            .Select(entry => (entry.File, entry.Line, entry.Hits!.Value))];

        if (coverable.Count < settings.MinChangedLines)
        {
            return new GateStep(StepName, 0, Verdict.NotApplicable, 0, $"{coverable.Count} coverable changed lines, below the {settings.MinChangedLines}-line minimum; not judged");
        }

        int covered = coverable.Count(entry => entry.Hits > 0);
        int percent = (int)Math.Floor(covered * 100.0 / coverable.Count);
        string summary = $"{percent}% of {coverable.Count} changed lines covered (threshold {settings.PatchThreshold}%)";
        if (percent >= settings.PatchThreshold)
        {
            return new GateStep(StepName, 0, Verdict.Pass, 0, summary);
        }

        string uncovered = string.Join(", ", coverable
            .Where(entry => entry.Hits == 0)
            .GroupBy(entry => entry.File)
            .Take(MaxUncoveredListed)
            .Select(group => $"{group.Key}:{string.Join(',', group.Select(entry => entry.Line.ToString(CultureInfo.InvariantCulture)).Take(MaxLinesListed))}"));
        return new GateStep(StepName, 0, Verdict.Fail, 0, $"{summary}; uncovered: {uncovered}");
    }
}
