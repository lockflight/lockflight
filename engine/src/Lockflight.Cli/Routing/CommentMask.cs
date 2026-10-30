using System.Text;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// C# source with comments and preprocessor lines blanked but string and character literals kept, same length and line breaks:
/// what counts as code for size. A line inside a multi-line string (a raw or verbatim literal holding SQL, JSON or an expected
/// value) is code; a line inside a block comment is not.
/// </summary>
internal static class CommentMask
{
    public static string Of(string source)
    {
        string masked = CSharpMask.Mask(source);
        StringBuilder text = new(source.Length);
        bool inLiteral = false;
        char quote = '\0';
        for (int index = 0; index < source.Length; index++)
        {
            char current = masked[index];
            if (!inLiteral && current is '"' or '\'' && source[index] == current)
            {
                inLiteral = true;
                quote = current;
            }
            else if (inLiteral && current == quote && source[index] == quote)
            {
                inLiteral = false;
            }
            else if (inLiteral)
            {
                current = source[index];
            }

            text.Append(current);
        }

        return text.ToString();
    }
}
