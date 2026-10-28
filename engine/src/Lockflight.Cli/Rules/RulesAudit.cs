using Lockflight.Cli.Config;
using Lockflight.Cli.Docs;

namespace Lockflight.Cli.Rules;

/// <summary>
/// The rules audit: every rule in the tracked instruction files (<see cref="DocumentSelection.IsRuleSource"/>), mapped to what
/// backs it (<see cref="RuleMapping"/>). Read only; no gate reads the result.
/// </summary>
internal static class RulesAudit
{
    private const string BannedSymbolsFile = "BannedSymbols.txt";

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

        List<AuditedRule> rules = [];
        foreach (string file in files.Tracked.Where(DocumentSelection.IsRuleSource).Order(StringComparer.Ordinal))
        {
            IReadOnlyList<MarkdownLine> lines = MarkdownDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Root, file), cancellationToken));
            rules.AddRange(RuleExtraction.Rules(file, lines).Select(rule => RuleMapping.Map(rule, protectedGlobs, bannedSymbols)));
        }

        return new RulesAuditReport(Scanned: true, rules);
    }

    /// <summary>The ids of a <c>BannedSymbols.txt</c>: each line up to its <c>;</c> message, without <c>//</c> comments.</summary>
    private static IReadOnlyList<string> SymbolIds(string[] lines) =>
        [.. lines.Select(line => line.Split("//")[0].Split(';')[0].Trim()).Where(id => id.Length > 0)];
}
