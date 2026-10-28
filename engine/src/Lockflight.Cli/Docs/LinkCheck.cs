using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>
/// Markdown links, images and reference definitions whose target is a repository file: the file or directory must exist, and a
/// <c>#fragment</c> into a Markdown file must name one of its headings. URLs and other schemes are not followed.
/// </summary>
internal static partial class LinkCheck
{
    public static IEnumerable<DriftFinding> Check(string document, MarkdownLine line, DriftContext context)
    {
        string text = InlineCode.Blanked(line.Text);
        IEnumerable<string> targets = InlinePattern().Matches(text).Select(match => match.Groups["target"].Value)
            .Concat(DefinitionPattern().Matches(text).Select(match => match.Groups["target"].Value));
        foreach (string written in targets)
        {
            if (Check(document, line.Number, written.Trim('<', '>'), context) is DriftFinding finding)
            {
                yield return finding;
            }
        }
    }

    private static DriftFinding? Check(string document, int lineNumber, string target, DriftContext context)
    {
        if (target.Length == 0 || SchemePattern().IsMatch(target) || target.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        int hash = target.IndexOf('#');
        string pathPart = Uri.UnescapeDataString(hash < 0 ? target : target[..hash]).Split('?')[0];
        string fragment = hash < 0 ? string.Empty : Uri.UnescapeDataString(target[(hash + 1)..]);
        string? resolved = pathPart.Length == 0 ? document
            : pathPart.StartsWith('/') ? RepoPath.Combine(string.Empty, pathPart)
            : RepoPath.Combine(RepoPath.Directory(document), pathPart);
        if (resolved is null)
        {
            return null;
        }

        if (!context.Files.Exists(resolved))
        {
            return new DriftFinding(document, lineNumber, target, "no such file", DriftKind.Link);
        }

        bool checksAnchor = fragment.Length > 0 && context.Files.IsFile(resolved) && resolved.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !LineAnchorPattern().IsMatch(fragment);
        return checksAnchor && !context.AnchorsOf(resolved).Contains(fragment)
            ? new DriftFinding(document, lineNumber, target, $"no heading '#{fragment}' in {RepoPath.FileName(resolved)}", DriftKind.Anchor)
            : null;
    }

    [GeneratedRegex(@"\]\(\s*(?<target><[^>]*>|[^)\s]+)(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)")]
    private static partial Regex InlinePattern();

    [GeneratedRegex(@"^\s{0,3}\[(?!\^)[^\]]+\]:\s*(?<target>\S+)")]
    private static partial Regex DefinitionPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemePattern();

    [GeneratedRegex(@"^L\d+")]
    private static partial Regex LineAnchorPattern();
}
