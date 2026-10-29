using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Processes;

/// <summary>
/// Reads the unified diff that <c>Git.DiffHunksAsync</c> produces (zero context, <c>a/</c> and <c>b/</c> prefixes). A hunk's
/// lines are counted from its header, so an added line whose text starts with <c>++ </c> is never mistaken for a file header.
/// </summary>
internal static partial class UnifiedDiff
{
    private const string OldFileHeader = "--- ";
    private const string NewFileHeader = "+++ ";
    private const string OldFilePrefix = "a/";
    private const string NewFilePrefix = "b/";

    /// <summary>
    /// Files the diff touches, in diff order, each with the lines it adds and removes; a deleted file is left out, and a hunk that only
    /// deletes yields its file with no added lines.
    /// </summary>
    public static IReadOnlyList<FileDiff> FilesOf(string diff) =>
        [.. PatchFilesOf(diff).Where(file => file.NewFile is not null).Select(file => new FileDiff(file.NewFile!, file.Added, file.Removed))];

    /// <summary>
    /// Every file the diff touches, in diff order, deleted files included: each with its old path (null when added) and new path
    /// (null when deleted) from the <c>---</c> and <c>+++</c> headers, and the lines it adds and removes. A file with no text
    /// hunks (binary, mode-only) has no headers and is not listed.
    /// </summary>
    public static IReadOnlyList<PatchFile> PatchFilesOf(string diff)
    {
        List<PatchFile> files = [];
        List<DiffLine> added = [];
        List<DiffLine> removed = [];
        string? oldFile = null;
        bool inFile = false;
        int oldRemaining = 0;
        int newRemaining = 0;
        int nextOld = 0;
        int nextNew = 0;
        foreach (string line in diff.Split('\n'))
        {
            if (oldRemaining > 0 && line.StartsWith('-'))
            {
                oldRemaining--;
                removed.Add(new DiffLine(nextOld++, line[1..].TrimEnd('\r')));
            }
            else if (newRemaining > 0 && line.StartsWith('+'))
            {
                newRemaining--;
                added.Add(new DiffLine(nextNew++, line[1..].TrimEnd('\r')));
            }
            else if (line.StartsWith(OldFileHeader, StringComparison.Ordinal))
            {
                oldFile = FileName(line[OldFileHeader.Length..], OldFilePrefix);
                inFile = false;
            }
            else if (line.StartsWith(NewFileHeader, StringComparison.Ordinal))
            {
                (added, removed) = ([], []);
                string? newFile = FileName(line[NewFileHeader.Length..], NewFilePrefix);
                inFile = newFile is not null || oldFile is not null;
                if (inFile)
                {
                    files.Add(new PatchFile(oldFile, newFile, added, removed));
                }

                oldFile = null;
            }
            else if (inFile && Hunk().Match(line) is { Success: true } hunk)
            {
                oldRemaining = Count(hunk.Groups["oldCount"]);
                newRemaining = Count(hunk.Groups["newCount"]);
                nextOld = int.Parse(hunk.Groups["oldStart"].Value, CultureInfo.InvariantCulture);
                nextNew = int.Parse(hunk.Groups["start"].Value, CultureInfo.InvariantCulture);
            }
        }

        return files;
    }

    /// <summary>The path after <paramref name="prefix"/> (<c>a/</c> or <c>b/</c>), unquoted when git quoted it; null for <c>/dev/null</c>.</summary>
    private static string? FileName(string text, string prefix)
    {
        // git appends a tab to a header whose name contains a space.
        string name = text.TrimEnd('\r').TrimEnd('\t');
        if (name.StartsWith('"') && name.EndsWith('"') && name.Length >= 2)
        {
            name = Unquote(name[1..^1]);
        }

        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : null;
    }

    /// <summary>git's C-style quoting: backslash escapes and octal bytes (UTF-8).</summary>
    private static string Unquote(string quoted)
    {
        List<byte> bytes = [];
        for (int index = 0; index < quoted.Length; index++)
        {
            char current = quoted[index];
            if (current != '\\' || index + 1 >= quoted.Length)
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(current.ToString()));
                continue;
            }

            char escaped = quoted[++index];
            if (escaped is >= '0' and <= '7' && index + 2 < quoted.Length)
            {
                bytes.Add(Convert.ToByte(quoted.Substring(index, 3), 8));
                index += 2;
                continue;
            }

            bytes.Add((byte)(escaped switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                _ => escaped,
            }));
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static int Count(Group group) => group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 1;

    [GeneratedRegex(@"^@@ -(?<oldStart>\d+)(?:,(?<oldCount>\d+))? \+(?<start>\d+)(?:,(?<newCount>\d+))? @@")]
    private static partial Regex Hunk();
}
