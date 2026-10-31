using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a pull request changes production code lines, the condition under which red/green, tests since red and spec/trace
/// apply at every class (risk routing §7): a changed line of a <c>production</c> file (C#, F#, Visual Basic, a <c>.cs</c> under no
/// project); a changed line of a <c>markup</c> file inside its C# logic (<see cref="RazorCodeBlocks"/>; a file whose text cannot be
/// read counts); a changed production or markup file routing treats as binary (binary content, or a file
/// too large to read; a binary file has no lines to judge); or a changed line of a test project that a production project references, because that
/// code ships, or of a project that was a production project at base. Project,
/// build-wide and config files do not count (a dependency bump needs no red); they raise their own routing signals. A file whose
/// code lines, in order, are the same at base and head changes no code: a pure rename (the moved file's old text against its new
/// text), a comment or XML doc edit, a reindent. Preprocessor lines count as code there, and a reorder is a change.
/// </summary>
internal static class ProductionChange
{
    public static bool Of(ChangeAnalysis analysis)
    {
        HashSet<string> shipped = ShippedTestProjects(analysis);
        return analysis.Files.Any(file => (file.Change.Binary || ChangesCode(analysis, file)) && file.Role switch
        {
            FileRole.Production => true,
            FileRole.Markup => file.Change.Binary || InLogic(file, analysis.ChangeSet),
            FileRole.Test => file.Project is string project && (shipped.Contains(project) || WasProduction(analysis, project)),
            _ => false,
        });
    }

    /// <summary>
    /// Whether the file's diff changes code: its code lines in order differ between its text at base (at the old path for a move)
    /// and at head (at the new path). The comparison is ordered, so lines the whitespace pairing dropped as a reorder still count.
    /// A text that cannot be read counts as a change.
    /// </summary>
    private static bool ChangesCode(ChangeAnalysis analysis, RoutedFile file)
    {
        string basePath = file.Move?.OldPath ?? file.Path;
        string headPath = file.Move?.NewPath ?? file.Path;
        string? before = analysis.ChangeSet.Base.Contains(basePath) ? analysis.ChangeSet.Base.TextOf(basePath) : string.Empty;
        string? after = analysis.ChangeSet.Head.Contains(headPath) ? analysis.ChangeSet.Head.TextOf(headPath) : string.Empty;
        return before is null || after is null || !CodeLines(file.Path, before).SequenceEqual(CodeLines(file.Path, after), StringComparer.Ordinal);
    }

    /// <summary>
    /// The file's non-blank code lines with whitespace runs collapsed: for C#, comments blanked (<see cref="CommentMask"/>) but a
    /// preprocessor line kept as written, since it decides what compiles; other files line by line as written.
    /// </summary>
    private static IEnumerable<string> CodeLines(string path, string text)
    {
        string[] lines = text.Split('\n');
        string[] code = ChangeAnalyzer.IsCSharp(path) ? CommentMask.Of(text).Split('\n') : lines;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = index < code.Length ? code[index] : lines[index];
            if (line.AsSpan().Trim().IsEmpty && lines[index].AsSpan().TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                line = lines[index];
            }

            string collapsed = WhitespacePairs.Collapse(line);
            if (collapsed.Length > 0)
            {
                yield return collapsed;
            }
        }
    }

    /// <summary>A project the pull request turned into a test project: its new files are still the production code they were.</summary>
    private static bool WasProduction(ChangeAnalysis analysis, string project) =>
        analysis.ChangeSet.Base.Contains(project) && !analysis.BaseRoles.IsTestProject(project);

    private static bool InLogic(RoutedFile file, ChangeSet changeSet) =>
        Touches(file.Added, changeSet.Head.TextOf(file.Path)) || Touches(file.Removed, changeSet.Base.TextOf(file.Path));

    private static bool Touches(IReadOnlyList<RoutedLine> lines, string? text)
    {
        if (lines.Count == 0)
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        IReadOnlySet<int> logic = RazorCodeBlocks.Lines(text);
        return lines.Any(line => logic.Contains(line.Line));
    }

    /// <summary>The test projects at head that a non-test project at head references.</summary>
    private static HashSet<string> ShippedTestProjects(ChangeAnalysis analysis)
    {
        RevisionSnapshot head = analysis.ChangeSet.Head;
        HashSet<string> shipped = new(StringComparer.Ordinal);
        foreach (string project in head.Paths.Where(ProjectIndex.IsProjectFile).Where(path => !analysis.HeadRoles.IsTestProject(path)))
        {
            if (head.TextOf(project) is not string text)
            {
                continue;
            }

            foreach (ProjectReference reference in ProjectReferences.Of(text, project))
            {
                if (reference.Target is string target && head.Contains(target) && analysis.HeadRoles.IsTestProject(target))
                {
                    shipped.Add(target);
                }
            }
        }

        return shipped;
    }
}
