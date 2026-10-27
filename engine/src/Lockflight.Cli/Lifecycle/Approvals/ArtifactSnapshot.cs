using System.Text;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// An artifact an approval binds, read once: <see cref="Content"/> is its bytes, null when it does not exist. The hash and the text
/// shown to the person both come from these bytes.
/// </summary>
internal sealed record ArtifactSnapshot(string Name, byte[]? Content)
{
    /// <summary>The hash recorded for an artifact that does not exist.</summary>
    public const string Absent = "absent";

    /// <summary>The <see cref="ArtifactHash"/> of the content, or <see cref="Absent"/>.</summary>
    public string Hash => Content is null ? Absent : ArtifactHash.Of(Content);

    /// <summary>The content decoded as <see cref="File.ReadAllText(string)"/> would (UTF-8 unless a byte order mark says otherwise); null when absent.</summary>
    public string? Text
    {
        get
        {
            if (Content is null)
            {
                return null;
            }

            using StreamReader reader = new(new MemoryStream(Content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }
}
