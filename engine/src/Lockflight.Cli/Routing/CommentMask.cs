using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// C# source with comments and preprocessor lines blanked but string and character literals kept, same length and line breaks:
/// what counts as code for size. A line inside a multi-line string (a raw or verbatim literal holding SQL, JSON or an expected
/// value) is code; a line inside a block comment is not.
/// </summary>
internal static class CommentMask
{
    public static string Of(string source) => CSharpMask.MaskComments(source, []);
}
