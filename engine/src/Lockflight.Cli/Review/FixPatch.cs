using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Reads a reviewer's fix, a unified diff, against the working tree without writing to it. Every file it names must be an existing
/// C# file of a non-test project inside the repository; a fix that adds, removes or renames a file, or touches test code, is
/// refused. Hunks are matched by their lines, not by the line numbers or counts in their headers (models get those wrong): the
/// context and removed lines must appear in the file in order after the previous hunk, exactly or apart from trailing whitespace,
/// and the occurrence nearest the header's line wins. A file keeps its line endings.
/// </summary>
internal static partial class FixPatch
{
    private const string NoFile = "/dev/null";

    public static FixPatchResult Prepare(string patch, string root)
    {
        if (Parse(patch) is not { } sections)
        {
            return FixPatchResult.Failed("the fix names no file: it needs `--- a/<path>` and `+++ b/<path>` lines with paths from the repository root, then `@@` hunks");
        }

        if (sections.Error is string parseError)
        {
            return FixPatchResult.Failed(parseError);
        }

        string top = Path.GetFullPath(root);
        List<FixedFile> files = [];
        foreach (IGrouping<string, FileSection> file in sections.Files.GroupBy(section => section.Path, StringComparer.Ordinal))
        {
            if (Target(file.Key, top) is not (string full, string project) target)
            {
                return FixPatchResult.Failed(Refusal(file.Key, top));
            }

            string original = File.ReadAllText(full);
            string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            List<string> lines = [.. original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
            int cursor = 0;
            int number = 0;
            foreach (Hunk hunk in file.SelectMany(section => section.Hunks))
            {
                number++;
                if (hunk.Old.Count == 0)
                {
                    return FixPatchResult.Failed($"{file.Key}: hunk {number} has no context or removed lines, so there is nothing to place it by");
                }

                if (Find(lines, hunk, cursor) is not int at)
                {
                    return FixPatchResult.Failed($"{file.Key}: hunk {number} does not match the file (its context and removed lines are not in it, in that order)");
                }

                lines.RemoveRange(at, hunk.Old.Count);
                lines.InsertRange(at, hunk.New);
                cursor = at + hunk.New.Count;
            }

            files.Add(new FixedFile(full, project, string.Join(newline, lines)));
        }

        return new FixPatchResult(files, null);
    }

    /// <summary>The file sections of the diff; null when it names no file, with an error when a section is not a change of one existing file.</summary>
    private static ParsedPatch? Parse(string patch)
    {
        string[] lines = patch.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<FileSection> sections = [];
        FileSection? current = null;
        Hunk? hunk = null;
        int blanks = 0;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.StartsWith("--- ", StringComparison.Ordinal) && index + 1 < lines.Length && lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal))
            {
                string old = PathOf(line, "a/");
                string updated = PathOf(lines[index + 1], "b/");
                if (old == NoFile || updated == NoFile)
                {
                    return new ParsedPatch([], $"{(old == NoFile ? updated : old)}: a fix may only change existing files, not add or remove one");
                }

                if (!string.Equals(old, updated, StringComparison.Ordinal))
                {
                    return new ParsedPatch([], $"{old}: a fix may not rename a file (to {updated})");
                }

                blanks = 0;
                current = new FileSection(updated, []);
                sections.Add(current);
                hunk = null;
                index++;
                continue;
            }

            if (current is null)
            {
                continue;
            }

            if (HunkHeader().Match(line) is { Success: true } header)
            {
                blanks = 0;
                hunk = new Hunk(int.Parse(header.Groups["start"].Value, CultureInfo.InvariantCulture), [], []);
                current.Hunks.Add(hunk);
                continue;
            }

            if (hunk is null || line.StartsWith('\\'))
            {
                continue;
            }

            // An empty line is an empty context line whose leading space was lost, unless nothing of the hunk follows it (the
            // diff's last newline, a blank line between files).
            if (line.Length == 0)
            {
                blanks++;
                continue;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                hunk = null;
                blanks = 0;
                continue;
            }

            if (line[0] is ' ' or '-' or '+')
            {
                hunk.Old.AddRange(Enumerable.Repeat(string.Empty, blanks));
                hunk.New.AddRange(Enumerable.Repeat(string.Empty, blanks));
            }

            blanks = 0;
            switch (line[0])
            {
                case ' ':
                    hunk.Old.Add(line[1..]);
                    hunk.New.Add(line[1..]);
                    break;
                case '-':
                    hunk.Old.Add(line[1..]);
                    break;
                case '+':
                    hunk.New.Add(line[1..]);
                    break;
                default:
                    hunk = null;
                    break;
            }
        }

        if (sections.Count == 0)
        {
            return null;
        }

        FileSection? empty = sections.FirstOrDefault(section => section.Hunks.Count == 0);
        return empty is null ? new ParsedPatch(sections, null) : new ParsedPatch([], $"{empty.Path}: the fix has no `@@` hunk for it");
    }

    /// <summary>Where the hunk's old lines start at or after <paramref name="cursor"/>: exact matches first, then ignoring trailing whitespace; the nearest to the header's line.</summary>
    private static int? Find(List<string> lines, Hunk hunk, int cursor)
    {
        int hint = Math.Max(hunk.Start - 1, cursor);
        foreach (Func<string, string, bool> same in new Func<string, string, bool>[] { string.Equals, (left, right) => left.TrimEnd() == right.TrimEnd() })
        {
            int[] starts = [.. Enumerable.Range(cursor, Math.Max(0, lines.Count - hunk.Old.Count - cursor + 1))
                .Where(start => hunk.Old.Select((line, offset) => same(lines[start + offset], line)).All(match => match))];
            if (starts.Length > 0)
            {
                return starts.MinBy(start => Math.Abs(start - hint));
            }
        }

        return null;
    }

    /// <summary>The file's full path and its project's file when the fix may change it; null otherwise.</summary>
    private static (string Full, string Project)? Target(string path, string top)
    {
        if (Path.IsPathRooted(path) || path.Split('/', '\\').Contains("..") || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string full = Path.GetFullPath(Path.Combine(top, path.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(full) && NearestTestProject.OwningProject(full, top) is string project && !TestProjectProfiles.IsTestProject(File.ReadAllText(project))
            ? (full, project)
            : null;
    }

    private static string Refusal(string path, string top)
    {
        if (Path.IsPathRooted(path) || path.Split('/', '\\').Contains(".."))
        {
            return $"{path}: a fix may only change files inside the repository, named from its root";
        }

        string full = Path.GetFullPath(Path.Combine(top, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            return $"{path}: a fix may only change existing C# files";
        }

        return NearestTestProject.OwningProject(full, top) is string project
            ? $"{path}: is in test project {Path.GetRelativePath(top, project).Replace('\\', '/')}; a fix may change only production code, never a test"
            : $"{path}: is not in a project";
    }

    /// <summary>The path of a <c>---</c> or <c>+++</c> line, without the <c>a/</c> or <c>b/</c> prefix and any timestamp after a tab.</summary>
    private static string PathOf(string line, string prefix)
    {
        string path = line[4..].Split('\t')[0].Trim();
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }

    [GeneratedRegex(@"^@@ -(?<start>\d+)(?:,\d+)? \+\d+(?:,\d+)? @@")]
    private static partial Regex HunkHeader();

    private sealed record ParsedPatch(IReadOnlyList<FileSection> Files, string? Error);

    private sealed record FileSection(string Path, List<Hunk> Hunks);

    private sealed record Hunk(int Start, List<string> Old, List<string> New);
}
