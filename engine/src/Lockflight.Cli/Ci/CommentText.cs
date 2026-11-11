using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ci;

/// <summary>
/// How text from a pull request (test names, requirement ids, file names, reviewer text) goes into the Markdown the judge posts:
/// as data. In a code span, backticks become quotes; outside one, HTML is escaped, and <c>@</c>, <c>://</c> and <c>www.</c> get a
/// zero-width space so a test name can neither ping a team nor become a link in the bot's comment; every line is capped. A block of
/// text (a diff) goes in a fence it cannot close.
/// </summary>
internal static partial class CommentText
{
    public const int MaxLineLength = 300;

    private const string ZeroWidthSpace = "​";

    private const int MinimumFence = 3;

    /// <summary>Text as an inline code span.</summary>
    public static string Code(string? text) => $"`{Line(text ?? string.Empty).Replace('`', '\'').Replace("|", "\\|", StringComparison.Ordinal)}`";

    /// <summary>Text as plain Markdown: HTML and Markdown control characters escaped, mentions and bare links defused.</summary>
    public static string Plain(string? text) => WwwPrefix().Replace(Line(text ?? string.Empty), match => match.Value[..^1] + ZeroWidthSpace + ".")
        .Replace("://", ":" + ZeroWidthSpace + "//", StringComparison.Ordinal)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("@", "@" + ZeroWidthSpace, StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("`", "'", StringComparison.Ordinal);

    /// <summary>
    /// Text as a fenced code block, its fence longer than any run of backticks in it so the text cannot close the block; one line
    /// break ends the text.
    /// </summary>
    public static string Fenced(string text, string language)
    {
        int longest = 0;
        int run = 0;
        foreach (char character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        string fence = new('`', Math.Max(MinimumFence, longest + 1));
        return $"{fence}{language}\n{text.TrimEnd('\n')}\n{fence}\n";
    }

    /// <summary><c>www.</c> at the start of a word, which GitHub turns into a link without a scheme.</summary>
    [GeneratedRegex(@"\bwww\.", RegexOptions.IgnoreCase)]
    private static partial Regex WwwPrefix();

    private static string Line(string text)
    {
        string single = text.ReplaceLineEndings(" ");
        return single.Length <= MaxLineLength ? single : single[..(MaxLineLength - 1)] + "…";
    }
}
