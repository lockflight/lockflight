using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Review;

/// <summary>
/// The changed files git reports as binary although their content is text. git decides what is binary from the file's
/// attributes before its content: a <c>.gitattributes</c> line such as <c>*.cs -diff</c> (or <c>binary</c>, or a diff driver set
/// to binary) makes it print "Binary files … differ" for a text file, so the reviewer would not see the change at all. A side
/// counts as text the way git judges it without attributes: no NUL byte in its first 8,000 bytes. The new side is read from the
/// working tree (the diff runs against it), the old side from the base, and a file is listed when either side is text.
/// </summary>
internal static class HiddenTextFiles
{
    private const int SniffBytes = 8_000;
    private const string Unknown = "-";

    /// <summary>
    /// The '/'-separated paths (with the repository's unit prefix) of the changed files git shows as binary that hold text; null when
    /// git cannot list the changed files.
    /// </summary>
    public static async Task<IReadOnlyList<string>?> FindAsync(RepoRoot repo, string baseRef, IReadOnlyList<string> scope, CancellationToken cancellationToken)
    {
        ProcessResult numstat = await ProcessRunner.RunAsync(
            "git", ["diff", "--numstat", "-z", "--no-color", "--no-ext-diff", "--no-textconv", baseRef, "--", .. scope], repo.Directory, cancellationToken);
        if (numstat.ExitCode != 0)
        {
            return null;
        }

        List<string> hidden = [];
        foreach ((string oldPath, string newPath) in BinaryEntries(numstat.Output))
        {
            if (IsText(Path.Combine(repo.Directory, newPath)) || await IsTextAtAsync(repo.Directory, baseRef, oldPath, cancellationToken))
            {
                hidden.Add(repo.Prefix + newPath);
            }
        }

        return hidden;
    }

    /// <summary>
    /// The old and new paths of the entries <c>git diff --numstat -z</c> counts as binary ("-" added and deleted): a plain entry is
    /// <c>-\t-\tpath\0</c>, a rename or copy <c>-\t-\t\0old\0new\0</c>.
    /// </summary>
    private static IEnumerable<(string Old, string New)> BinaryEntries(string numstat)
    {
        string[] fields = numstat.Split('\0');
        int index = 0;
        while (index < fields.Length)
        {
            string[] counts = fields[index].Split('\t', 3);
            index++;
            if (counts.Length < 3)
            {
                continue;
            }

            (string oldPath, string newPath) = (counts[2], counts[2]);
            if (counts[2].Length == 0 && index + 1 < fields.Length)
            {
                (oldPath, newPath) = (fields[index], fields[index + 1]);
                index += 2;
            }

            if (counts[0] == Unknown && counts[1] == Unknown)
            {
                yield return (oldPath, newPath);
            }
        }
    }

    /// <summary>Whether the working-tree file exists and holds no NUL byte in its first 8,000 bytes.</summary>
    private static bool IsText(string file)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        using FileStream stream = File.OpenRead(file);
        byte[] head = new byte[SniffBytes];
        int read = stream.ReadAtLeast(head, SniffBytes, throwOnEndOfStream: false);
        return !head.AsSpan(0, read).Contains((byte)0);
    }

    /// <summary>Whether the file exists at the base and holds no NUL byte in its first 8,000 characters (as git prints the blob).</summary>
    private static async Task<bool> IsTextAtAsync(string directory, string baseRef, string path, CancellationToken cancellationToken)
    {
        ProcessResult blob = await ProcessRunner.RunAsync("git", ["cat-file", "blob", $"{baseRef}:{path}"], directory, cancellationToken);
        return blob.ExitCode == 0 && !blob.Output.AsSpan(0, Math.Min(blob.Output.Length, SniffBytes)).Contains('\0');
    }
}
