using System.Text;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Turns file bytes into the text routing reads: null for binary content (a NUL byte in the first 8000 bytes, git's own test) or a
/// file above <see cref="MaxBytes"/>; otherwise UTF-8 with the byte order mark dropped and CRLF line endings as LF.
/// </summary>
internal static class RevisionText
{
    /// <summary>The largest file routing reads as text; a larger one is treated like a binary file.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    private const int BinaryProbeBytes = 8000;
    private const char ByteOrderMark = '﻿';

    public static bool LooksBinary(ReadOnlySpan<byte> content) => content[..Math.Min(content.Length, BinaryProbeBytes)].Contains((byte)0);

    public static string? Decode(byte[] content)
    {
        if (content.Length > MaxBytes || LooksBinary(content))
        {
            return null;
        }

        string text = Encoding.UTF8.GetString(content);
        return text.TrimStart(ByteOrderMark).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
