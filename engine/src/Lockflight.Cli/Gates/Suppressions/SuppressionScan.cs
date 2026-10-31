using System.Text.RegularExpressions;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>
/// The suppressions a change adds (<see cref="SuppressionPatterns"/>): the lines it adds from the merge base with the base ref (or,
/// without one, from the last commit) to the working tree, in every working tree of a workspace, and every line of an untracked file.
/// Suppressions already in the code are not judged, and a suppression moved within a file is not new: per file, rule and kind, only the
/// added ones beyond those the change removes count. An analyzer config line counts when it sets a severity below warning or below
/// what the same setting said before. Generated files are left out by path and shape (<see cref="GeneratedFiles"/>: lock files,
/// EF Core model snapshots and migration designers), and so is the <c>#nullable disable</c> that EF Core writes above the namespace
/// of a migration whose designer file is EF Core's, or which carries EF Core's migration attributes itself.
/// </summary>
internal static partial class SuppressionScan
{
    private const string MigrationsDirectory = "Migrations";
    private const string DesignerSuffix = ".Designer.cs";

    public static async Task<SuppressionScanResult> FindAsync(string projectRoot, string? baseRef, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        if (await WorkspaceGit.DiffToWorkingTreeAsync(projectRoot, baseRef, SuppressionPatterns.Pathspecs, timeLimit, cancellationToken) is not string diff)
        {
            return SuppressionScanResult.Failed(baseRef is null
                ? "git could not diff the working tree; the suppressions check needs a git repository"
                : $"git could not diff against the merge base with '{baseRef}'; is the checkout deep enough (fetch-depth: 0), and does every unit repository have that ref?");
        }

        if (await WorkspaceGit.UntrackedFilesAsync(projectRoot, SuppressionPatterns.Pathspecs, timeLimit, cancellationToken) is not IReadOnlyList<string> untracked)
        {
            return SuppressionScanResult.Failed("git could not list the untracked files; the suppressions check needs a git repository");
        }

        List<FileDiff> files = [.. UnifiedDiff.FilesOf(diff).Where(file => SuppressionPatterns.CanHoldSuppressions(file.File))];
        foreach (string path in untracked.Where(SuppressionPatterns.CanHoldSuppressions))
        {
            files.Add(new FileDiff(path, AllLines(projectRoot, path), []));
        }

        HashSet<string> changed = [.. files.Select(file => file.File)];
        List<Suppression> found = [];
        foreach (FileDiff file in files)
        {
            found.AddRange(InFile(projectRoot, file, changed));
        }

        return new SuppressionScanResult([.. found.OrderBy(item => item.File, StringComparer.Ordinal).ThenBy(item => item.Line)], files.Count, Problem: null);
    }

    private static IEnumerable<Suppression> InFile(string root, FileDiff file, IReadOnlySet<string> changed)
    {
        string fullPath = Path.Combine(root, file.File);
        if (file.Added.Count == 0 || !File.Exists(fullPath) || new FileInfo(fullPath).LinkTarget is not null
            || GeneratedFiles.Find(file.File, changed, root) is not null)
        {
            return [];
        }

        string[] lines = File.ReadAllText(fullPath).Split('\n');
        string[] masked = SuppressionPatterns.IsCSharp(file.File) ? SuppressionPatterns.MaskedCSharpLines(string.Join('\n', lines)) : lines;
        List<(DiffLine Line, SuppressionHit Hit)> removed = [.. file.Removed
            .SelectMany(line => SuppressionPatterns.InLine(file.File, line.Text, MaskedLine(file.File, line.Text)).Select(hit => (line, hit)))];
        List<(DiffLine Line, SuppressionHit Hit)> added = [.. file.Added
            .Where(line => line.Line >= 1 && line.Line <= lines.Length)
            .SelectMany(line => SuppressionPatterns.InLine(file.File, lines[line.Line - 1], masked[line.Line - 1]).Select(hit => (line, hit)))];

        int efHeaderEnd = IsEfMigration(root, file.File, masked) ? NamespaceLine(masked) : 0;
        IEnumerable<(DiffLine Line, SuppressionHit Hit)> addedSuppressions = added
            .Where(entry => Silences(entry.Hit) || Lowers(entry.Hit, removed.Select(old => old.Hit)))
            .Where(entry => !(entry.Hit.Kind == SuppressionKind.NullableDisable && entry.Line.Line < efHeaderEnd));
        List<SuppressionHit> removedSuppressions = [.. removed.Select(entry => entry.Hit).Where(Silences)];

        return addedSuppressions
            .GroupBy(entry => (entry.Hit.Rule, entry.Hit.Kind))
            .SelectMany(group => group.Skip(removedSuppressions.Count(hit => hit.Rule == group.Key.Rule && hit.Kind == group.Key.Kind)))
            .Select(entry => new Suppression(file.File, entry.Line.Line, entry.Hit.Rule, entry.Hit.Kind));
    }

    /// <summary>Whether a hit silences on its own: every kind except an analyzer config line at warning or above.</summary>
    private static bool Silences(SuppressionHit hit) => hit.Severity is not int severity || severity < SuppressionPatterns.WarningSeverity;

    /// <summary>Whether an analyzer config line sets a lower severity than a line of the same setting the change removes.</summary>
    private static bool Lowers(SuppressionHit hit, IEnumerable<SuppressionHit> removed) =>
        hit.Severity is int severity && removed.Any(old => old.SettingKey == hit.SettingKey && old.Severity > severity);

    private static string MaskedLine(string path, string text) =>
        SuppressionPatterns.IsCSharp(path) ? SuppressionPatterns.MaskedCSharpLines(text)[0] : text;

    private static DiffLine[] AllLines(string root, string path)
    {
        string fullPath = Path.Combine(root, path);
        return File.Exists(fullPath) ? [.. File.ReadAllText(fullPath).Split('\n').Select((text, index) => new DiffLine(index + 1, text.TrimEnd('\r')))] : [];
    }

    // The 1-based number of the first namespace line, masked; 0 when there is none.
    private static int NamespaceLine(string[] masked) => Array.FindIndex(masked, line => NamespaceStart().IsMatch(line)) + 1;

    // An EF Core migration: Migrations/<name>.cs whose <name>.Designer.cs is EF Core's generated designer, or, with no designer,
    // a file whose code (comments and strings masked) carries [DbContext(…)] and [Migration(…)] and declares one class only, which
    // derives from Migration. Either way only a #nullable disable above the namespace, where EF Core writes it, is left out.
    private static bool IsEfMigration(string root, string path, string[] masked)
    {
        if (!SuppressionPatterns.IsCSharp(path) || path.EndsWith(DesignerSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        int slash = path.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : path[..slash];
        if (directory != MigrationsDirectory && !directory.EndsWith("/" + MigrationsDirectory, StringComparison.Ordinal))
        {
            return false;
        }

        string designer = Path.Combine(root, path[..^".cs".Length] + DesignerSuffix);
        return File.Exists(designer)
            ? EfGeneratedCode.IsMigrationDesigner(File.ReadAllText(designer))
            : CarriesMigrationAttributes(string.Join('\n', masked));
    }

    private static bool CarriesMigrationAttributes(string code) =>
        DbContextAttribute().IsMatch(code)
        && MigrationAttribute().IsMatch(code)
        && TypeDeclaration().Count(code) == 1
        && MigrationClass().IsMatch(code);

    [GeneratedRegex(@"^\s*namespace\s")]
    private static partial Regex NamespaceStart();

    [GeneratedRegex(@"\[\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Infrastructure\.)?DbContext(?:Attribute)?\s*\(\s*typeof\s*\(")]
    private static partial Regex DbContextAttribute();

    [GeneratedRegex(@"[\[,]\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Migrations\.)?Migration(?:Attribute)?\s*\(\s*""")]
    private static partial Regex MigrationAttribute();

    [GeneratedRegex(@"\b(?:class|struct|interface|enum|record|delegate)\s+\w")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"\bclass\s+\w+\s*:\s*(?:global::)?(?:Microsoft\.EntityFrameworkCore\.Migrations\.)?Migration\b")]
    private static partial Regex MigrationClass();
}
