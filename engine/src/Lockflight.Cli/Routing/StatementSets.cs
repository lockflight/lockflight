using System.Text.RegularExpressions;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Compares statements that are not declarations (<c>app.MapGet(…)</c>, <c>modelBuilder.…</c>, <c>builder.Property(…)</c>) per
/// project, as multisets of whole statements: a statement runs to its <c>;</c>, <c>{</c> or <c>}</c> outside brackets, so a call
/// written over several lines or followed by <c>.RequireAuthorization(…)</c> on the next line is one statement. A statement
/// counts when its masked text (comments and strings blanked) matches the pattern, and is compared by its text with comments
/// blanked and whitespace collapsed (a changed route template is a change; a changed comment is not). A statement moved between
/// files of a project, reordered or reformatted fires nothing. Returns where the first statement one side has and the other
/// lacks starts; null when none.
/// </summary>
internal static class StatementSets
{
    public static string? FirstDifference(ChangeAnalysis analysis, IReadOnlyCollection<FileRole> roles, Regex pattern)
    {
        Dictionary<string, int> balance = new(StringComparer.Ordinal);
        Dictionary<string, string> whereOf = new(StringComparer.Ordinal);
        foreach (RoutedFile file in analysis.Files.Where(file => ChangeAnalyzer.IsCSharp(file.Path)))
        {
            if (file.AtBase is FileRoleAt atBase && roles.Contains(atBase.Role) && analysis.ChangeSet.Base.TextOf(file.Path) is string baseText)
            {
                Count(balance, whereOf, ProjectKey(analysis, atBase.Project), file.Path, baseText, pattern, -1);
            }

            if (file.AtHead is FileRoleAt atHead && roles.Contains(atHead.Role) && analysis.ChangeSet.Head.TextOf(file.Path) is string headText)
            {
                Count(balance, whereOf, atHead.Project ?? string.Empty, file.Path, headText, pattern, +1);
            }
        }

        return balance.Where(entry => entry.Value != 0).Select(entry => whereOf[entry.Key]).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    private static void Count(Dictionary<string, int> balance, Dictionary<string, string> whereOf, string project, string path, string text, Regex pattern, int sign)
    {
        string masked = CSharpMask.Mask(text);
        string comments = CommentMask.Of(text);
        int depth = 0;
        int start = 0;
        for (int index = 0; index <= masked.Length; index++)
        {
            char current = index < masked.Length ? masked[index] : ';';
            if (current is '(' or '[')
            {
                depth++;
                continue;
            }

            if (current is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (depth > 0 || current is not (';' or '{' or '}'))
            {
                continue;
            }

            int end = Math.Min(index + 1, masked.Length);
            if (pattern.IsMatch(masked[start..end]))
            {
                string key = $"{project}\u0001{WhitespacePairs.Collapse(comments[start..end])}";
                balance[key] = balance.GetValueOrDefault(key) + sign;
                int first = start;
                while (first < end && char.IsWhiteSpace(masked[first]))
                {
                    first++;
                }

                string where = $"{path}:{1 + text.AsSpan(0, first).Count('\n')}";
                if (!whereOf.TryGetValue(key, out string? known) || (sign > 0 && string.CompareOrdinal(where, known) < 0))
                {
                    whereOf[key] = where;
                }
            }

            start = index + 1;
        }
    }

    private static string ProjectKey(ChangeAnalysis analysis, string? project) =>
        project is null ? string.Empty : analysis.Moves.FirstOrDefault(move => move.OldPath == project)?.NewPath ?? project;
}
