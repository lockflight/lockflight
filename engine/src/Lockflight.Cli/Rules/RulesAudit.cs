using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Docs;

namespace Lockflight.Cli.Rules;

/// <summary>
/// The rules audit: every rule in the tracked instruction files (<see cref="DocumentSelection.IsRuleSource"/>), mapped to what
/// backs it (<see cref="RuleMapping"/>). Read only; no gate reads the result.
/// </summary>
internal static partial class RulesAudit
{
    private const string BannedSymbolsFile = "BannedSymbols.txt";
    private const string EditorConfigFile = ".editorconfig";
    private const string GlobalConfigExtension = ".globalconfig";
    private const string CSharpExtension = ".cs";
    private static readonly string[] ArchitectureLibraries = ["using ArchUnitNET", "using NetArchTest"];

    public static async Task<RulesAuditReport> RunAsync(string directory, CancellationToken cancellationToken)
    {
        if (await RepositoryFiles.LoadAsync(directory, cancellationToken) is not RepositoryFiles files)
        {
            return RulesAuditReport.NotARepository;
        }

        IReadOnlyList<string>? protectedGlobs = ManifestLocator.Discover(directory) is { Manifest: Manifest manifest } ? DefaultProtectedPaths.For(manifest) : null;
        Dictionary<string, IReadOnlyList<string>> bannedSymbols = new(StringComparer.Ordinal);
        foreach (string path in files.Tracked.Where(path => RepoPath.FileName(path) == BannedSymbolsFile))
        {
            bannedSymbols[path] = SymbolIds(await File.ReadAllLinesAsync(Path.Combine(files.Root, path), cancellationToken));
        }

        ProjectChecks checks = new(protectedGlobs, bannedSymbols, await AnalyzersAsync(files, cancellationToken), await ArchitectureTestsAsync(files, cancellationToken));
        List<AuditedRule> rules = [];
        foreach (string file in files.Tracked.Where(DocumentSelection.IsRuleSource).Order(StringComparer.Ordinal))
        {
            IReadOnlyList<MarkdownLine> lines = MarkdownDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Root, file), cancellationToken));
            rules.AddRange(RuleExtraction.Rules(file, lines).Select(rule => RuleMapping.Map(rule, checks)));
        }

        return new RulesAuditReport(Scanned: true, rules);
    }

    /// <summary>
    /// Every <c>dotnet_diagnostic.&lt;id&gt;.severity</c> in the tracked <c>.editorconfig</c> and <c>.globalconfig</c> files. Where
    /// several set one id, a setting that reaches the build wins: the audit says what can back a rule, not which file wins.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, AnalyzerSetting>> AnalyzersAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        Dictionary<string, AnalyzerSetting> settings = new(StringComparer.Ordinal);
        foreach (string path in files.Tracked.Where(IsAnalyzerConfig).Order(StringComparer.Ordinal))
        {
            foreach (Match match in SeverityLine().Matches(await File.ReadAllTextAsync(Path.Combine(files.Root, path), cancellationToken)))
            {
                AnalyzerSetting setting = new(match.Groups["id"].Value.ToUpperInvariant(), match.Groups["severity"].Value.ToLowerInvariant(), path);
                if (!settings.TryGetValue(setting.Id, out AnalyzerSetting? earlier) || (!earlier.ReachesTheBuild && setting.ReachesTheBuild))
                {
                    settings[setting.Id] = setting;
                }
            }
        }

        return settings;
    }

    /// <summary>The tracked C# files in test code that use an architecture testing library.</summary>
    private static async Task<IReadOnlyList<string>> ArchitectureTestsAsync(RepositoryFiles files, CancellationToken cancellationToken)
    {
        List<string> tests = [];
        foreach (string path in files.Tracked.Where(path => path.EndsWith(CSharpExtension, StringComparison.OrdinalIgnoreCase) && path.Contains("test", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            string text = await File.ReadAllTextAsync(Path.Combine(files.Root, path), cancellationToken);
            if (ArchitectureLibraries.Any(library => text.Contains(library, StringComparison.Ordinal)))
            {
                tests.Add(path);
            }
        }

        return tests;
    }

    private static bool IsAnalyzerConfig(string path)
    {
        string name = RepoPath.FileName(path);
        return name == EditorConfigFile || name.EndsWith(GlobalConfigExtension, StringComparison.Ordinal);
    }

    /// <summary>The ids of a <c>BannedSymbols.txt</c>: each line up to its <c>;</c> message, without <c>//</c> comments.</summary>
    private static IReadOnlyList<string> SymbolIds(string[] lines) =>
        [.. lines.Select(line => line.Split("//")[0].Split(';')[0].Trim()).Where(id => id.Length > 0)];

    [GeneratedRegex(@"^[ \t]*dotnet_diagnostic\.(?<id>[A-Za-z]+[0-9]+)\.severity[ \t]*=[ \t]*(?<severity>[A-Za-z]+)", RegexOptions.Multiline)]
    private static partial Regex SeverityLine();
}
