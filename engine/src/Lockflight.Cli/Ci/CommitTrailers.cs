namespace Lockflight.Cli.Ci;

/// <summary>
/// One commit of a pull request's history as the judge reads it: id, parents, commit time (ISO 8601 as git prints it), the
/// values of its <c>Lockflight-Red</c> and <c>Lockflight-Amend</c> trailers (real trailers only, never lines in the body) and
/// its subject. The trailers only say where to look; nothing here is trusted.
/// </summary>
internal sealed record CommitTrailers(string Commit, IReadOnlyList<string> Parents, string CommittedAt, IReadOnlyList<string> Red, IReadOnlyList<string> Amend, string Subject)
{
    private const int FieldsPerCommit = 6;
    private const char ValueSeparator = '\u0001';

    /// <summary>Parses <c>git log -z</c> output of <see cref="JudgeGit.HistoryAsync"/>; null when it does not have that shape.</summary>
    public static IReadOnlyList<CommitTrailers>? ParseLog(string output)
    {
        if (output.Length == 0)
        {
            return [];
        }

        // Each field ends with NUL: five from the format, the sixth from -z after every commit.
        string[] fields = (output.EndsWith('\0') ? output[..^1] : output).Split('\0');
        if (fields.Length % FieldsPerCommit != 0)
        {
            return null;
        }

        List<CommitTrailers> commits = [];
        for (int index = 0; index < fields.Length; index += FieldsPerCommit)
        {
            string commit = fields[index].TrimStart('\n');
            if (!CommitIds.IsFull(commit))
            {
                return null;
            }

            commits.Add(new CommitTrailers(
                commit,
                fields[index + 1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                fields[index + 2],
                Values(fields[index + 3]),
                Values(fields[index + 4]),
                fields[index + 5]));
        }

        return commits;
    }

    private static string[] Values(string field) =>
        [.. field.Split(ValueSeparator).Select(value => value.Trim()).Where(value => value.Length > 0)];
}
