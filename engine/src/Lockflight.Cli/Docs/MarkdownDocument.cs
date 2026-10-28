using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>A Markdown document split into lines that know whether they are prose, a heading or inside a fenced block.</summary>
internal static partial class MarkdownDocument
{
    /// <summary>The comment that keeps the drift check off one line, for a deliberately wrong example.</summary>
    public const string IgnoreMarker = "<!-- lockflight: drift-ignore -->";

    public static IReadOnlyList<MarkdownLine> Parse(string text)
    {
        List<MarkdownLine> lines = [];
        string[] rawLines = text.ReplaceLineEndings("\n").Split('\n');
        string? fence = null;
        string language = string.Empty;
        for (int index = 0; index < rawLines.Length; index++)
        {
            string line = rawLines[index];
            Match marker = FencePattern().Match(line);
            MarkdownLineKind kind;
            if (fence is not null)
            {
                bool closes = marker.Success && marker.Groups["fence"].Value.StartsWith(fence, StringComparison.Ordinal) && marker.Groups["info"].Value.Length == 0;
                kind = closes ? MarkdownLineKind.FenceMarker : MarkdownLineKind.Fenced;
                lines.Add(new MarkdownLine(index + 1, line, kind, language));
                if (closes)
                {
                    fence = null;
                    language = string.Empty;
                }

                continue;
            }

            if (marker.Success)
            {
                fence = marker.Groups["fence"].Value;
                language = marker.Groups["info"].Value.ToLowerInvariant();
                lines.Add(new MarkdownLine(index + 1, line, MarkdownLineKind.FenceMarker, language));
                continue;
            }

            kind = line.Contains(IgnoreMarker, StringComparison.Ordinal) ? MarkdownLineKind.Ignored
                : HeadingPattern().IsMatch(line) ? MarkdownLineKind.Heading
                : MarkdownLineKind.Prose;
            lines.Add(new MarkdownLine(index + 1, line, kind, string.Empty));
        }

        return lines;
    }

    /// <summary>The text of an ATX heading line without its <c>#</c> marks; null for any other line.</summary>
    public static string? HeadingText(string line) =>
        HeadingPattern().Match(line) is { Success: true } heading ? heading.Groups["text"].Value.Trim() : null;

    /// <summary>The level of an ATX heading line (1 for <c>#</c>); 0 for any other line.</summary>
    public static int HeadingLevel(string line) =>
        HeadingPattern().Match(line) is { Success: true } heading ? heading.Groups["marks"].Value.Length : 0;

    [GeneratedRegex(@"^\s*(?<fence>`{3,}|~{3,})\s*(?<info>[^\s`]*)")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^ {0,3}(?<marks>#{1,6})[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$")]
    private static partial Regex HeadingPattern();
}
