using System.Globalization;
using System.Text;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// Makes text printable for a person: a file name, a test name, a spec line or a ledger field comes from the project, and a
/// control character or escape sequence in it could move the cursor, rewrite a line (<c>PASS\rFAIL</c>), recolour or hide text,
/// retitle the window or set the clipboard (OSC 52), so the terminal would show something other than what the engine decided.
/// Each such character is printed visibly instead: caret notation for C0 controls and DEL (<c>^[</c> for ESC), <c>&lt;U+XXXX&gt;</c>
/// for C1 controls and the bidirectional overrides that reorder text. Line feeds, tabs and CR LF line ends stay as they are.
/// </summary>
internal static class TerminalText
{
    private const char LineFeed = '\n';
    private const char CarriageReturn = '\r';
    private const char Tab = '\t';
    private const char Delete = '\u007f';
    private const char CaretOffset = '@';

    /// <summary>The text with every character that would act on the terminal made visible; the same instance when there is none.</summary>
    public static string Clean(string text) => IndexOfUnsafe(text) < 0 ? text : Clean(text.AsSpan());

    /// <summary>As <see cref="Clean(string)"/>, for a span.</summary>
    public static string Clean(ReadOnlySpan<char> text)
    {
        StringBuilder clean = new(text.Length + 8);
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (!IsUnsafe(text, index))
            {
                clean.Append(character);
            }
            else if (character < ' ')
            {
                clean.Append('^').Append((char)(character + CaretOffset));
            }
            else if (character == Delete)
            {
                clean.Append("^?");
            }
            else
            {
                clean.Append(CultureInfo.InvariantCulture, $"<U+{(int)character:X4}>");
            }
        }

        return clean.ToString();
    }

    /// <summary>
    /// Whether the text holds no control character at all (line feeds and tabs included) and no bidirectional override: a name a
    /// person can read exactly as it is.
    /// </summary>
    public static bool IsPlainName(string text) => !text.Any(character => char.IsControl(character) || IsBidirectionalOverride(character));

    private static int IndexOfUnsafe(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (IsUnsafe(text, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsUnsafe(ReadOnlySpan<char> text, int index)
    {
        char character = text[index];
        return character switch
        {
            LineFeed or Tab => false,
            CarriageReturn => index + 1 >= text.Length || text[index + 1] != LineFeed,
            _ => char.IsControl(character) || IsBidirectionalOverride(character),
        };
    }

    /// <summary>The embeddings, overrides and isolates (U+202A–U+202E, U+2066–U+2069) that make text display in another order.</summary>
    private static bool IsBidirectionalOverride(char character) => character is (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');
}
