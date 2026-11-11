namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Leaves out the HTML comments a Spec Kit document holds its template guidance in, line by line: a comment that starts a line
/// runs to its <c>--&gt;</c>, on that line or a later one. What follows the <c>--&gt;</c> on the same line is shown when the
/// document is rendered, so it is read too; a comment that only starts later in a line is left as text, as it was.
/// </summary>
internal sealed class MarkdownComments
{
    private const string Open = "<!--";
    private const string Close = "-->";

    private bool inComment;

    /// <summary>The part of <paramref name="line"/> outside comments; empty when all of it is inside one.</summary>
    public string Visible(string line)
    {
        string rest = line;
        while (true)
        {
            if (inComment)
            {
                int close = rest.IndexOf(Close, StringComparison.Ordinal);
                if (close < 0)
                {
                    return string.Empty;
                }

                inComment = false;
                rest = rest[(close + Close.Length)..];
                continue;
            }

            string trimmed = rest.TrimStart();
            if (!trimmed.StartsWith(Open, StringComparison.Ordinal))
            {
                return rest;
            }

            inComment = true;
            rest = trimmed[Open.Length..];
        }
    }
}
