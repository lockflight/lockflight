using System.Text.RegularExpressions;
using Lockflight.Cli.Docs;

namespace Lockflight.Cli.Rules;

/// <summary>
/// Finds the rules in an instruction file, deterministically. Wrapped lines are joined first: a list item runs until the next
/// item, a blank line, a heading or a fence; a paragraph until a blank line. In a list item or table cell, a sentence with an
/// upper-case marker (<c>MUST</c>, <c>NEVER</c>, <c>ALWAYS</c>, <c>SHALL</c>, <c>DO NOT</c>, <c>REQUIRED</c>) or a lower-case one
/// (<c>never</c>, <c>always</c>, <c>must</c>, <c>do not</c>, <c>don't</c>) is a rule; in a paragraph, a sentence with an upper-case
/// marker or starting with a lower-case one. Headings and fenced blocks are not read.
/// </summary>
internal static partial class RuleExtraction
{
    private const int MaxTextLength = 160;

    public static IEnumerable<RuleSentence> Rules(string file, IReadOnlyList<MarkdownLine> lines)
    {
        foreach ((bool isItem, List<MarkdownLine> block) in Blocks(lines))
        {
            foreach ((int lineNumber, string sentence) in Sentences(block))
            {
                bool rule = UpperPattern().IsMatch(sentence)
                    || (isItem ? LowerPattern().IsMatch(InlineCode.Blanked(sentence)) : LowerStartPattern().IsMatch(Plain(sentence)));
                if (rule)
                {
                    yield return new RuleSentence(file, lineNumber, Shorten(Plain(sentence)), InlineCode.Spans(sentence));
                }
            }
        }
    }

    /// <summary>List items, table cells and paragraphs, each as the lines it spans (a table row's cells as separate one-line blocks).</summary>
    private static IEnumerable<(bool IsItem, List<MarkdownLine> Lines)> Blocks(IReadOnlyList<MarkdownLine> lines)
    {
        List<MarkdownLine> current = [];
        bool currentIsItem = false;
        foreach (MarkdownLine line in lines)
        {
            string trimmed = line.Text.Trim();
            bool breaks = line.Kind != MarkdownLineKind.Prose || trimmed.Length == 0 || trimmed.StartsWith('|') || ListMarkerPattern().IsMatch(trimmed);
            if (breaks && current.Count > 0)
            {
                yield return (currentIsItem, current);
                current = [];
            }

            if (line.Kind != MarkdownLineKind.Prose || trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('|'))
            {
                foreach (string cell in trimmed.Trim('|').Split('|').Select(cell => cell.Trim()).Where(cell => cell.Length > 0))
                {
                    yield return (true, [line with { Text = cell }]);
                }

                continue;
            }

            if (ListMarkerPattern().Match(trimmed) is { Success: true } marker)
            {
                currentIsItem = true;
                current.Add(line with { Text = trimmed[marker.Length..] });
            }
            else
            {
                currentIsItem = current.Count > 0 && currentIsItem;
                current.Add(line with { Text = trimmed });
            }
        }

        if (current.Count > 0)
        {
            yield return (currentIsItem, current);
        }
    }

    /// <summary>The block's sentences, each with the number of the line it starts on.</summary>
    private static IEnumerable<(int Line, string Sentence)> Sentences(List<MarkdownLine> block)
    {
        string text = string.Join(' ', block.Select(line => line.Text));
        int[] starts = new int[block.Count];
        for (int index = 1; index < block.Count; index++)
        {
            starts[index] = starts[index - 1] + block[index - 1].Text.Length + 1;
        }

        int start = 0;
        foreach (Match? boundary in SentenceBoundaryPattern().Matches(text).Append(null))
        {
            int end = boundary?.Index ?? text.Length;
            string sentence = text[start..end].Trim();
            if (sentence.Length > 0)
            {
                int lineIndex = Array.FindLastIndex(starts, offset => offset <= start);
                yield return (block[Math.Max(lineIndex, 0)].Number, sentence);
            }

            start = boundary is null ? text.Length : boundary.Index + boundary.Length;
        }
    }

    private static string Plain(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Trim();

    private static string Shorten(string text) => text.Length <= MaxTextLength ? text : text[..(MaxTextLength - 1)] + "…";

    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?")]
    private static partial Regex ListMarkerPattern();

    /// <summary>A sentence end: <c>.</c>, <c>!</c> or <c>?</c> (after an optional closing <c>**</c>) and space before a capital, bold or code.</summary>
    [GeneratedRegex(@"(?<=[.!?](?:\*\*)?)\s+(?=[A-Z*`])")]
    private static partial Regex SentenceBoundaryPattern();

    [GeneratedRegex(@"\b(?:MUST|NEVER|ALWAYS|SHALL|DO NOT|REQUIRED)\b")]
    private static partial Regex UpperPattern();

    [GeneratedRegex(@"\b(?:never|always|must|do not|don't)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LowerPattern();

    [GeneratedRegex(@"^(?:never|always|must|do not|don't|only)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LowerStartPattern();
}
