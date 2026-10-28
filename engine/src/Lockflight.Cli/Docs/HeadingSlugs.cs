using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Docs;

/// <summary>
/// The anchors a Markdown document offers, as GitHub renders them: each heading's slug (lower case, punctuation dropped, spaces
/// to hyphens, <c>-1</c>, <c>-2</c> for repeats) plus explicit <c>&lt;a id&gt;</c> and <c>&lt;a name&gt;</c> anchors.
/// </summary>
internal static partial class HeadingSlugs
{
    public static IReadOnlySet<string> Of(IReadOnlyList<MarkdownLine> lines)
    {
        HashSet<string> anchors = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> seen = new(StringComparer.Ordinal);
        foreach (MarkdownLine line in lines)
        {
            if (line.Kind is MarkdownLineKind.Fenced or MarkdownLineKind.FenceMarker)
            {
                continue;
            }

            foreach (Match anchor in ExplicitAnchorPattern().Matches(line.Text))
            {
                anchors.Add(anchor.Groups["id"].Value);
            }

            if (line.Kind == MarkdownLineKind.Heading && MarkdownDocument.HeadingText(line.Text) is string text)
            {
                string slug = Slug(text);
                anchors.Add(seen.TryGetValue(slug, out int count) ? $"{slug}-{count}" : slug);
                seen[slug] = count + 1;
            }
        }

        return anchors;
    }

    public static string Slug(string headingText)
    {
        string text = LinkPattern().Replace(headingText, "${text}");
        text = TagPattern().Replace(text, string.Empty).ToLowerInvariant();
        StringBuilder slug = new(text.Length);
        foreach (char character in text)
        {
            if (character == ' ')
            {
                slug.Append('-');
            }
            else if (character is '-' or '_' || char.IsLetterOrDigit(character) || char.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark)
            {
                slug.Append(character);
            }
        }

        return slug.ToString();
    }

    [GeneratedRegex(@"\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"<a\s+(?:[^>]*\s)?(?:id|name)=""(?<id>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitAnchorPattern();
}
