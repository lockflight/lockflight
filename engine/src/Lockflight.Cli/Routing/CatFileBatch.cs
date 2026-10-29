using System.Globalization;
using System.Text;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads the output of <c>git cat-file --batch</c>: per request either <c>&lt;id&gt; &lt;type&gt; &lt;size&gt;\n</c> followed by
/// <c>size</c> bytes and a line break, or one line ending in <c>missing</c> or <c>ambiguous</c>. Only blobs are kept.
/// </summary>
internal static class CatFileBatch
{
    private const string BlobType = "blob";

    /// <summary>The content of each requested path, in request order; a request git could not answer with a blob is left out.</summary>
    public static IReadOnlyDictionary<string, byte[]> Parse(byte[] output, IReadOnlyList<string> requested)
    {
        Dictionary<string, byte[]> contents = new(StringComparer.Ordinal);
        int position = 0;
        foreach (string path in requested)
        {
            int lineEnd = Array.IndexOf(output, (byte)'\n', position);
            if (lineEnd < 0)
            {
                break;
            }

            string[] header = Encoding.UTF8.GetString(output, position, lineEnd - position).Split(' ');
            position = lineEnd + 1;
            if (header.Length != 3 || !long.TryParse(header[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size))
            {
                continue;
            }

            if (size > output.Length - position)
            {
                break;
            }

            if (header[1] == BlobType)
            {
                contents[path] = output[position..(position + (int)size)];
            }

            position += (int)size + 1;
        }

        return contents;
    }
}
