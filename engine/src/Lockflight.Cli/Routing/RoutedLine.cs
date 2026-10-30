namespace Lockflight.Cli.Routing;

/// <summary>
/// A changed line as routing reads it: its 1-based number (at head when added, at base when removed), its text, and its code — the
/// text with comments and preprocessor lines blanked for C# (<see cref="CommentMask"/> over the whole file; strings kept), the
/// text itself otherwise.
/// </summary>
internal sealed record RoutedLine(int Line, string Text, string Code)
{
    /// <summary>Whether the line holds code: a blank, comment-only or preprocessor-only line does not count toward size.</summary>
    public bool Counts => Code.AsSpan().Trim().Length > 0;
}
