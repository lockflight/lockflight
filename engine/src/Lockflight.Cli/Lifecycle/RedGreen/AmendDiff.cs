using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// What an amend changed in its files since the red, recorded when its red is accepted: the unified diff against the red-time
/// snapshot (cut after <see cref="MaxLines"/> lines), the SHA-256 (hex) of the whole diff, and a one-line summary
/// ("+1 -1 lines in tests/A.cs").
/// </summary>
internal sealed record AmendDiff(string Text, string Sha256, string Summary)
{
    /// <summary>The longest diff kept in the record and shown in the pull request; the hash still covers all of it.</summary>
    public const int MaxLines = 200;

    public static AmendDiff From(string diff, IReadOnlyList<string> files)
    {
        string[] lines = diff.Split('\n');
        (int added, int removed) = Count(lines);
        string text = lines.Length <= MaxLines
            ? diff
            : string.Join('\n', lines.Take(MaxLines)) + $"\n… {lines.Length - MaxLines} more lines; the hash covers the whole diff\n";
        return new AmendDiff(text, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(diff))), $"+{added} -{removed} lines in {string.Join(", ", files)}");
    }

    /// <summary>Added and removed lines inside the hunks; a file's header lines (<c>--- a/…</c>, <c>+++ b/…</c>) come before its first hunk.</summary>
    private static (int Added, int Removed) Count(string[] lines)
    {
        int added = 0;
        int removed = 0;
        bool inHunk = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("diff ", StringComparison.Ordinal))
            {
                inHunk = false;
            }
            else if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
            }
            else if (inHunk && line.StartsWith('+'))
            {
                added++;
            }
            else if (inHunk && line.StartsWith('-'))
            {
                removed++;
            }
        }

        return (added, removed);
    }
}
