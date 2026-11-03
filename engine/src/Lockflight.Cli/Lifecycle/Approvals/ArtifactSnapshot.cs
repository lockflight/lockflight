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

    /// <summary>The artifact <c>tasks.md</c>, whose ticked task boxes do not change its hash.</summary>
    public const string TasksName = "tasks.md";

    /// <summary>
    /// The <see cref="ArtifactHash"/> of the content (<see cref="ArtifactHash.OfTasks"/> for <c>tasks.md</c>), or <see cref="Absent"/>.
    /// </summary>
    public string Hash => Content is null ? Absent : Name == TasksName ? ArtifactHash.OfTasks(Content) : ArtifactHash.Of(Content);

    /// <summary>
    /// The hash of the content as written, ticked boxes included: what engines before 0.3.1 recorded for <c>tasks.md</c>. The same as
    /// <see cref="Hash"/> for every other artifact.
    /// </summary>
    public string AsWrittenHash => Content is null ? Absent : ArtifactHash.Of(Content);

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
