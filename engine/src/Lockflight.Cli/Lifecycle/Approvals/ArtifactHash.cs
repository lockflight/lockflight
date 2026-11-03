using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// SHA-256 of an artifact's bytes with only two normalisations, so a checkout with <c>core.autocrlf</c> on Windows and one on Linux
/// hash the same spec: leading UTF-8 BOMs are dropped (a UTF-16 or UTF-32 file with a BOM is first transcoded to UTF-8, as
/// earlier engines read it), and CRLF and a lone CR become LF. Everything else counts as written — invalid
/// UTF-8, U+2028, U+0085 or a form feed change the hash. For UTF-8 text this equals the hash of the decoded text with its line endings
/// normalised, which earlier engines recorded.
/// </summary>
internal static class ArtifactHash
{
    private const byte CarriageReturn = (byte)'\r';
    private const byte LineFeed = (byte)'\n';

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>UTF-32 before UTF-16: a UTF-32 LE mark starts with the UTF-16 LE one.</summary>
    private static readonly (byte[] Bom, Encoding Encoding)[] WideEncodings =
    [
        ([0xFF, 0xFE, 0x00, 0x00], new UTF32Encoding(bigEndian: false, byteOrderMark: true)),
        ([0x00, 0x00, 0xFE, 0xFF], new UTF32Encoding(bigEndian: true, byteOrderMark: true)),
        ([0xFF, 0xFE], new UnicodeEncoding(bigEndian: false, byteOrderMark: true)),
        ([0xFE, 0xFF], new UnicodeEncoding(bigEndian: true, byteOrderMark: true)),
    ];

    public static string Of(string path) => Of(File.ReadAllBytes(path));

    public static string Of(ReadOnlySpan<byte> content)
    {
        byte[] normalised = Normalise(content);
        return Convert.ToHexStringLower(SHA256.HashData(normalised));
    }

    /// <summary>
    /// As <see cref="Of(ReadOnlySpan{byte})"/>, for <c>tasks.md</c>: a task line's box hashes as open whether it is ticked or not
    /// (<c>- [x] T012 …</c> as <c>- [ ] T012 …</c>), so ticking off tasks during implementation leaves the tasks approval valid. Any
    /// other change, including a ticked box on a line that is not a task, still changes the hash. A file without a ticked task hashes
    /// as <see cref="Of(ReadOnlySpan{byte})"/> does.
    /// </summary>
    public static string OfTasks(ReadOnlySpan<byte> content)
    {
        byte[] normalised = Normalise(content);
        int lineStart = 0;
        for (int index = 0; index <= normalised.Length; index++)
        {
            if (index == normalised.Length || normalised[index] == LineFeed)
            {
                if (TickedTaskBox(normalised, lineStart, index) is int box)
                {
                    normalised[box] = (byte)' ';
                }

                lineStart = index + 1;
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(normalised));
    }

    /// <summary>
    /// The index of the <c>x</c> in a task line's ticked box (<c>- [x] T001</c>, as <see cref="TasksDocument"/> reads a task: blanks,
    /// a dash, blanks, the box, blanks, <c>T</c>, three or more digits and a blank); null for any other line. ASCII only, so the bytes are
    /// matched as written.
    /// </summary>
    private static int? TickedTaskBox(byte[] text, int start, int end)
    {
        int index = SkipBlanks(text, start, end);
        if (index >= end || text[index] != (byte)'-')
        {
            return null;
        }

        int open = SkipBlanks(text, index + 1, end);
        if (open == index + 1 || open + 2 >= end || text[open] != (byte)'[' || text[open + 1] is not ((byte)'x' or (byte)'X') || text[open + 2] != (byte)']')
        {
            return null;
        }

        int id = SkipBlanks(text, open + 3, end);
        if (id == open + 3 || id >= end || text[id] != (byte)'T')
        {
            return null;
        }

        int digits = id + 1;
        while (digits < end && text[digits] is >= (byte)'0' and <= (byte)'9')
        {
            digits++;
        }

        return digits - id - 1 >= 3 && SkipBlanks(text, digits, end) > digits ? open + 1 : null;
    }

    private static int SkipBlanks(byte[] text, int index, int end)
    {
        while (index < end && text[index] is (byte)' ' or (byte)'\t')
        {
            index++;
        }

        return index;
    }

    /// <summary>The content without leading BOMs and with CRLF and a lone CR as LF.</summary>
    private static byte[] Normalise(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> text = FromUtf16Or32(content) ?? content;
        while (text.StartsWith(Utf8Bom))
        {
            text = text[Utf8Bom.Length..];
        }

        byte[] normalised = new byte[text.Length];
        int length = 0;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != CarriageReturn)
            {
                normalised[length++] = text[index];
                continue;
            }

            normalised[length++] = LineFeed;
            if (index + 1 < text.Length && text[index + 1] == LineFeed)
            {
                index++;
            }
        }

        return normalised[..length];
    }

    /// <summary>
    /// A file that starts with a UTF-16 or UTF-32 byte order mark, decoded with that encoding (as earlier engines read it) and
    /// re-encoded as UTF-8; null for any other file.
    /// </summary>
    private static byte[]? FromUtf16Or32(ReadOnlySpan<byte> content)
    {
        foreach ((byte[] bom, Encoding encoding) in WideEncodings)
        {
            if (content.StartsWith(bom))
            {
                return Encoding.UTF8.GetBytes(encoding.GetString(content[bom.Length..]));
            }
        }

        return null;
    }
}
