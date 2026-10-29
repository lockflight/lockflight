namespace Lockflight.Cli.Ci;

/// <summary>
/// How text from a pull request (test names, requirement ids, file names, reviewer text) goes into the Markdown the judge posts:
/// as data. In a code span, backticks become quotes; outside one, HTML is escaped and <c>@</c> gets a zero-width space so a test
/// name cannot ping a team; every line is capped.
/// </summary>
internal static class CommentText
{
    public const int MaxLineLength = 300;

    private const string ZeroWidthSpace = "​";

    /// <summary>Text as an inline code span.</summary>
    public static string Code(string? text) => $"`{Line(text ?? string.Empty).Replace('`', '\'').Replace("|", "\\|", StringComparison.Ordinal)}`";

    /// <summary>Text as plain Markdown: HTML and Markdown control characters escaped, mentions defused.</summary>
    public static string Plain(string? text) => Line(text ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("@", "@" + ZeroWidthSpace, StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("*", "\\*", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("`", "'", StringComparison.Ordinal);

    private static string Line(string text)
    {
        string single = text.ReplaceLineEndings(" ");
        return single.Length <= MaxLineLength ? single : single[..(MaxLineLength - 1)] + "…";
    }
}
