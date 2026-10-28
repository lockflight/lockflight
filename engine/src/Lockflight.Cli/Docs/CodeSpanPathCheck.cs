using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>
/// Paths in inline code spans. Most such spans in instructions name a file of some other project, a file a tool creates at run
/// time or an example, so a span that does not resolve is reported only with evidence of drift: the repository's history deleted
/// or renamed that path, or the file exists under the same name next door (a near miss). A path resolves against the document's
/// directory or any ancestor, so a sample's instructions may name paths inside the sample.
/// </summary>
internal static partial class CodeSpanPathCheck
{
    private static readonly string[] SourceExtensions =
        [".cs", ".csproj", ".fs", ".fsproj", ".sln", ".slnx", ".props", ".targets", ".sh", ".ps1", ".py", ".md", ".json", ".yml", ".yaml", ".toml", ".xml", ".txt", ".js", ".ts"];

    public static async Task<DriftFinding?> CheckAsync(string document, int lineNumber, string span, DriftContext context)
    {
        if (Candidate(span) is not string path)
        {
            return null;
        }

        string trimmed = path.TrimEnd('/');
        bool bareName = !trimmed.Contains('/');
        if (bareName ? context.Files.Named(trimmed).Any() : Resolutions(document, trimmed).Any(context.Files.Exists))
        {
            return null;
        }

        string? reason = bareName ? await BareNameReasonAsync(trimmed, context) : await PathReasonAsync(document, trimmed, context);
        return reason is null ? null : new DriftFinding(document, lineNumber, span, reason, DriftKind.Path);
    }

    /// <summary>The path a span names, without a <c>:line</c> or <c>#L</c> suffix and a leading <c>./</c>; null when the span is not shaped like a path.</summary>
    private static string? Candidate(string span)
    {
        string path = LineSuffixPattern().Replace(span.Trim(), string.Empty);
        if (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        bool shaped = PathPattern().IsMatch(path) && !path.Contains("//", StringComparison.Ordinal) && !path.StartsWith('/') && !path.StartsWith('-');
        bool pathLike = path.Contains('/') || SourceExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && path.Length > extension.Length);
        return shaped && pathLike ? path : null;
    }

    private static IEnumerable<string> Resolutions(string document, string path) =>
        RepoPath.SelfAndAncestors(RepoPath.Directory(document)).Select(directory => RepoPath.Combine(directory, path)).OfType<string>().Distinct(StringComparer.Ordinal);

    private static async Task<string?> PathReasonAsync(string document, string path, DriftContext context)
    {
        PathHistory history = await context.HistoryAsync();
        foreach (string candidate in Resolutions(document, path))
        {
            if (history.Removal(candidate) is PathRemoval removal)
            {
                return Describe(removal);
            }
        }

        foreach (string candidate in Resolutions(document, path))
        {
            string directory = RepoPath.Directory(candidate);
            if (directory.Length > 0 && context.Files.DirectoryExists(directory)
                && context.Files.Named(RepoPath.FileName(candidate)).OrderByDescending(other => SharedPrefix(other, candidate)).FirstOrDefault() is string elsewhere)
            {
                return $"not here; a file of this name is at {elsewhere}";
            }
        }

        return null;
    }

    private static async Task<string?> BareNameReasonAsync(string fileName, DriftContext context)
    {
        PathHistory history = await context.HistoryAsync();
        return history.RemovedNamed(fileName) is (string path, PathRemoval removal) ? $"no file of this name now: {path} {Describe(removal)}" : null;
    }

    private static string Describe(PathRemoval removal) =>
        removal.RenamedTo is string renamed ? $"renamed to {renamed} in {removal.Commit}" : $"deleted in {removal.Commit}";

    private static int SharedPrefix(string first, string second)
    {
        int length = 0;
        while (length < first.Length && length < second.Length && first[length] == second[length])
        {
            length++;
        }

        return length;
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-/+@]+$")]
    private static partial Regex PathPattern();

    [GeneratedRegex(@"(?::\d+(?:[-:]\d+)?|#L\d+.*)$")]
    private static partial Regex LineSuffixPattern();
}
