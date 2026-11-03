using System.Globalization;
using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Docs;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Facts;

/// <summary>
/// Collects <see cref="ProjectFacts"/> from a git working tree: the manifest, the gate profiles, and the tracked analyzer,
/// banned-symbol and code metrics configuration, instruction files and architecture tests. Read only, and the same tree gives the
/// same facts (every list is sorted).
/// </summary>
internal static partial class FactsCollector
{
    private const string BannedSymbolsFileName = "BannedSymbols.txt";
    private const string CodeMetricsFileName = "CodeMetricsConfig.txt";
    private const string EditorConfigFile = ".editorconfig";
    private const string GlobalConfigExtension = ".globalconfig";
    private const string CSharpExtension = ".cs";
    private static readonly string[] ArchitectureLibraries = ["using ArchUnitNET", "using NetArchTest"];
    private static readonly string[] TestAttributes = ["[Fact", "[Theory", "[Test"];

    /// <summary>The facts of the tree at <paramref name="directory"/>; null when it is not inside a git working tree.</summary>
    public static async Task<ProjectFacts?> CollectAsync(string directory, CancellationToken cancellationToken)
    {
        if (await RepositoryFiles.LoadAsync(directory, cancellationToken) is not RepositoryFiles files)
        {
            return null;
        }

        Manifest? manifest = ManifestLocator.Discover(directory).Manifest;
        GateSettings settings = manifest?.Gates ?? GateSettings.Default;
        GateProfileFact[] profiles = [.. GateProfiles.All.Select(definition => new GateProfileFact(
            GateProfileNames.ToName(definition.Profile),
            [.. definition.InOrder.Select(step => new GateStepFact(step.Name, step.Scope, step.ModeUnder(settings)))]))];
        string[] humanOnly = [.. CommandRegistry.All
            .Where(command => command is { Audience: CommandAudience.Human, Listing: CommandListing.Listed })
            .Select(command => command.Name)
            .Order(StringComparer.Ordinal)];
        return new ProjectFacts(
            manifest,
            manifest is null ? null : DefaultProtectedPaths.For(manifest),
            profiles,
            [.. files.Tracked.Where(DocumentSelection.IsRuleSource).Order(StringComparer.Ordinal)],
            await AnalyzersAsync(files, cancellationToken),
            await BannedSymbolsAsync(files, cancellationToken),
            await MetricThresholdsAsync(files, cancellationToken),
            await ArchitectureTestsAsync(files, cancellationToken),
            humanOnly,
            LedgerFiles(manifest?.Root ?? files.Root));
    }

    /// <summary>Every <c>dotnet_diagnostic.&lt;id&gt;.severity</c> line in the tracked <c>.editorconfig</c> and <c>.globalconfig</c> files, by file then id.</summary>
    private static async Task<IReadOnlyList<AnalyzerSetting>> AnalyzersAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        List<AnalyzerSetting> settings = [];
        foreach (string path in files.Tracked.Where(IsAnalyzerConfig).Order(StringComparer.Ordinal))
        {
            string text = await File.ReadAllTextAsync(Path.Combine(files.Root, path), cancellationToken);
            settings.AddRange(SeverityLine().Matches(text)
                .Select(match => new AnalyzerSetting(match.Groups["id"].Value.ToUpperInvariant(), match.Groups["severity"].Value.ToLowerInvariant(), path)));
        }

        return [.. settings.OrderBy(setting => setting.Id, StringComparer.Ordinal).ThenBy(setting => setting.File, StringComparer.Ordinal)];
    }

    private static async Task<IReadOnlyList<BannedSymbolsFile>> BannedSymbolsAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        List<BannedSymbolsFile> banned = [];
        foreach (string path in files.Tracked.Where(path => RepoPath.FileName(path) == BannedSymbolsFileName).Order(StringComparer.Ordinal))
        {
            string[] lines = await File.ReadAllLinesAsync(Path.Combine(files.Root, path), cancellationToken);
            banned.Add(new BannedSymbolsFile(path, [.. lines.Select(line => line.Split("//")[0].Split(';')[0].Trim()).Where(id => id.Length > 0)]));
        }

        return banned;
    }

    private static async Task<IReadOnlyList<MetricThreshold>> MetricThresholdsAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        List<MetricThreshold> thresholds = [];
        foreach (string path in files.Tracked.Where(path => RepoPath.FileName(path) == CodeMetricsFileName).Order(StringComparer.Ordinal))
        {
            string text = await File.ReadAllTextAsync(Path.Combine(files.Root, path), cancellationToken);
            foreach (Match match in ThresholdLine().Matches(text))
            {
                if (int.TryParse(match.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                {
                    string? target = match.Groups["target"].Success ? match.Groups["target"].Value : null;
                    thresholds.Add(new MetricThreshold(path, match.Groups["rule"].Value.ToUpperInvariant(), target, value));
                }
            }
        }

        return thresholds;
    }

    /// <summary>
    /// The tracked C# files in test code that use an architecture testing library, and the files that declare tests in an
    /// architecture test project (a directory named like <c>Shop.ArchitectureTests</c>, the name <c>init --architecture-tests</c>
    /// gives it) or are named like one (<c>ArchitectureTests.cs</c>), whose tests may read the source instead of using a library.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ArchitectureTestsAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        List<string> tests = [];
        foreach (string path in files.Tracked
            .Where(path => path.EndsWith(CSharpExtension, StringComparison.OrdinalIgnoreCase) && path.Contains("test", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            string text = await File.ReadAllTextAsync(Path.Combine(files.Root, path), cancellationToken);
            if (UsesArchitectureLibrary(text) || (NamedForArchitectureTests(path) && DeclaresTests(text)))
            {
                tests.Add(path);
            }
        }

        return tests;
    }

    private static bool UsesArchitectureLibrary(string text) => ArchitectureLibraries.Any(library => text.Contains(library, StringComparison.Ordinal));

    private static bool DeclaresTests(string text) => TestAttributes.Any(attribute => text.Contains(attribute, StringComparison.Ordinal));

    /// <summary>A directory on the path, or the file itself without <c>.cs</c>, is named like an architecture test project.</summary>
    private static bool NamedForArchitectureTests(string path) =>
        path[..^CSharpExtension.Length].Split('/').Any(name => ArchitectureTestsName().IsMatch(name));

    private static IReadOnlyList<string> LedgerFiles(string projectRoot)
    {
        string directory = Path.Combine(projectRoot, LedgerWriter.RelativeDirectory);
        return Directory.Exists(directory)
            ? [.. Directory.GetFiles(directory, LedgerWriter.FilePattern).Select(path => $"{LedgerWriter.RelativeDirectory}/{Path.GetFileName(path)}").Order(StringComparer.Ordinal)]
            : [];
    }

    private static bool IsAnalyzerConfig(string path)
    {
        string name = RepoPath.FileName(path);
        return name == EditorConfigFile || name.EndsWith(GlobalConfigExtension, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^[ \t]*dotnet_diagnostic\.(?<id>[A-Za-z]+[0-9]+)\.severity[ \t]*=[ \t]*(?<severity>[A-Za-z]+)", RegexOptions.Multiline)]
    private static partial Regex SeverityLine();

    [GeneratedRegex(@"^[ \t]*(?<rule>CA[0-9]{4})(?:\((?<target>[A-Za-z]+)\))?[ \t]*:[ \t]*(?<value>[0-9]{1,9})[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ThresholdLine();

    // Shop.ArchitectureTests, Shop.Architecture.Tests, ArchitectureTests, Shop.ArchTests (and the singular Test).
    [GeneratedRegex(@"^(.+\.)?Arch(itecture)?\.?Tests?$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchitectureTestsName();
}
