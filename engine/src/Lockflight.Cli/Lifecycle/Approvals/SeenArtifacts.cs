using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The hashes of a feature's <c>spec.md</c>, <c>plan.md</c> and <c>tasks.md</c> as the person could last see them in one Claude Code
/// session: when the agent's last turn in that session ended (the Stop hook of an attended, interactive session takes them), or as
/// <c>/lockflight:approve</c> last showed them instead of approving. The command records an approval only while the artifacts still
/// have these hashes and it is typed in the same session, so content the agent changed after its turn ended, in a turn that was
/// interrupted, or through another session, is shown to the person before it can be approved. Kept in <see cref="RelativePath"/>
/// (git-ignored, under the protected <c>.lockflight/</c>) and trusted for nothing else.
/// </summary>
internal sealed class SeenArtifacts(string root)
{
    public const string RelativeDirectory = ".lockflight/local";

    public const string FileName = "approval-seen.json";

    private const string Schema = "lockflight.approval-seen/v2";
    private const string TurnEndedName = "turn-ended";
    private const string ShownName = "shown";

    public static string RelativePath => $"{RelativeDirectory}/{FileName}";

    private string FullPath => Path.Combine(root, RelativeDirectory, FileName);

    /// <summary>
    /// Why the record cannot be kept safely: a part of <see cref="RelativeDirectory"/> or the file itself is a symbolic link, a file
    /// where a directory belongs, or a directory where the file belongs. Null when it can.
    /// </summary>
    public string? StateProblem() => FileProblem(root, FileName);

    /// <summary>As <see cref="StateProblem"/>, for any engine-written file <paramref name="fileName"/> in <see cref="RelativeDirectory"/>.</summary>
    public static string? FileProblem(string root, string fileName)
    {
        if (ProjectDirectories.Problem(root, RelativeDirectory) is string problem)
        {
            return problem;
        }

        string path = Path.Combine(root, RelativeDirectory, fileName);
        string shown = $"{RelativeDirectory}/{fileName}";
        return new FileInfo(path).LinkTarget is not null ? $"{shown} is a symbolic link"
            : Directory.Exists(path) ? $"{shown} is a directory, not a file"
            : null;
    }

    /// <summary>
    /// Records <paramref name="artifacts"/> of <paramref name="feature"/> as seen in session <paramref name="sessionId"/>. A turn end
    /// replaces the record; content shown by the command replaces only the artifacts it showed, so the others keep what the turn end
    /// recorded. Writes nothing while <see cref="StateProblem"/> names one or the session is unknown.
    /// </summary>
    public void Remember(Feature feature, string? sessionId, IEnumerable<(string Name, string Hash)> artifacts, SeenWhen when)
    {
        if (StateProblem() is not null || sessionId is null)
        {
            return;
        }

        JsonObject hashes = when == SeenWhen.Shown && Read() is { } earlier && earlier.Feature == feature.Id && earlier.Session == sessionId
            ? earlier.Artifacts
            : [];
        foreach ((string name, string hash) in artifacts)
        {
            hashes[name] = hash;
        }

        JsonObject record = new()
        {
            ["schema"] = Schema,
            ["feature"] = feature.Id,
            ["session"] = sessionId,
            ["when"] = when == SeenWhen.TurnEnded ? TurnEndedName : ShownName,
            ["artifacts"] = hashes.DeepClone(),
        };
        WriteAtomically(FullPath, JsonFiles.Format(record));
    }

    /// <summary>Removes the record (no feature is active at the end of a turn); does nothing while <see cref="StateProblem"/> names one.</summary>
    public void Forget()
    {
        if (StateProblem() is null)
        {
            File.Delete(FullPath);
        }
    }

    /// <summary>
    /// Compares <paramref name="current"/> with the record. Null when there is no readable record of <paramref name="feature"/> taken
    /// in session <paramref name="sessionId"/> (none was taken, it is unreadable, or it is of another feature or session).
    /// </summary>
    public SeenCheck? Compare(Feature feature, string? sessionId, IReadOnlyList<(string Name, string Hash)> current)
    {
        if (StateProblem() is not null || sessionId is null || Read() is not { } record || record.Feature != feature.Id || record.Session != sessionId)
        {
            return null;
        }

        string[] changed = [.. current.Where(artifact => Text(record.Artifacts[artifact.Name]) != artifact.Hash).Select(artifact => artifact.Name)];
        return new SeenCheck(record.When, changed);
    }

    /// <summary>
    /// Writes <paramref name="content"/> to a new file beside <paramref name="path"/> and moves it into place, so a reader never sees
    /// a partial file and a link left at the path is replaced, not followed.
    /// </summary>
    public static void WriteAtomically(string path, string content)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        using (FileStream file = new(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            file.Write(Encoding.UTF8.GetBytes(content));
        }

        File.Move(temporary, path, overwrite: true);
    }

    private Record? Read()
    {
        if (!File.Exists(FullPath))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(FullPath)) is JsonObject record
                && Text(record["schema"]) == Schema
                && Text(record["feature"]) is string feature
                && Text(record["session"]) is string session
                && record["artifacts"] is JsonObject artifacts
                && Text(record["when"]) switch { TurnEndedName => SeenWhen.TurnEnded, ShownName => SeenWhen.Shown, _ => (SeenWhen?)null } is SeenWhen when
                ? new Record(feature, session, when, (JsonObject)artifacts.DeepClone())
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private sealed record Record(string Feature, string Session, SeenWhen When, JsonObject Artifacts);
}
