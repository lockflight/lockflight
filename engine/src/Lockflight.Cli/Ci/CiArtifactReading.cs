using System.Text.Json.Nodes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Another CI job's JSON artifact as the report read it: the <see cref="Document"/>, or the <see cref="Problem"/> that made the
/// report refuse a file that is there; both null when there is no file (the job did not finish or did not run).
/// </summary>
internal sealed record CiArtifactReading(JsonObject? Document, string? Problem)
{
    public static CiArtifactReading None { get; } = new(null, null);
}
