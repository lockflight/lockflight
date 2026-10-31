using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a pull request changes production code lines, the condition under which red/green, tests since red and spec/trace
/// apply at every class (risk routing §7): a changed line of a <c>production</c> file (C#, F#, Visual Basic, a <c>.cs</c> under no
/// project); a changed line of a <c>markup</c> file inside its C# logic (<see cref="RazorCodeBlocks"/>; a file whose text cannot be
/// read counts); a changed production or markup file git treats as binary (a <c>.gitattributes</c> line in the pull request can make it so, and a
/// binary file has no lines to judge); or a changed line of a test project that a production project references, because that
/// code ships, or of a project that was a production project at base. Project,
/// build-wide and config files do not count (a dependency bump needs no red); they raise their own routing signals. A file whose
/// code lines, in order, are the same at base and head changes no code: a pure rename (the moved file's old text against its new
/// text), an edit of lines that hold only a comment or XML doc, a reindent. A reorder is a change, and so is a move of a markup file or
/// one into or out of <c>bin/</c> or <c>obj/</c>.
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
        // Where a markup file or a file under bin/ or obj/ lives is part of what it does (view and _ViewStart lookup, the
        // component's namespace, whether it compiles), so a move of one always counts.
        if (file.Move is FileMove move && (file.Role == FileRole.Markup || UnderBuildOutput(move.OldPath) || UnderBuildOutput(move.NewPath)))
        {
            return true;
        }

        string basePath = file.Move?.OldPath ?? file.Path;
        string headPath = file.Move?.NewPath ?? file.Path;
        string? before = analysis.ChangeSet.Base.Contains(basePath) ? analysis.ChangeSet.Base.TextOf(basePath) : string.Empty;
        string? after = analysis.ChangeSet.Head.Contains(headPath) ? analysis.ChangeSet.Head.TextOf(headPath) : string.Empty;
        return before is null || after is null || !CodeLines(file.Path, before).SequenceEqual(CodeLines(file.Path, after), StringComparer.Ordinal);
    }

    /// <summary>
    /// The file's lines as written, whitespace runs collapsed, without blank lines and, for C#, without lines that hold only a
    /// comment: the mask (<see cref="CommentMask"/>) blanks the line and the line itself reads as one (<c>//</c>, <c>/*</c>,
    /// <c>*</c>, <c>*/</c>). Both must agree, so a line of a string literal the mask got wrong is kept, and an edit of a comment
    /// that shares a line with code still counts. Other files keep every line.
    /// </summary>
    private static IEnumerable<string> CodeLines(string path, string text)
    {
        string[] lines = text.Split('\n');
        string[] code = ChangeAnalyzer.IsCSharp(path) ? CommentMask.Of(text).Split('\n') : lines;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = WhitespacePairs.Collapse(lines[index]);
            if (line.Length > 0 && !(code.Length == lines.Length && code[index].AsSpan().Trim().IsEmpty && LooksLikeAComment(line)))
            {
                yield return line;
            }
        }
    }

    private static bool UnderBuildOutput(string path) => path.Split('/').Any(segment => segment is "bin" or "obj");

    private static bool LooksLikeAComment(string line) =>
        line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("/*", StringComparison.Ordinal)
        || line.StartsWith('*') || line.EndsWith("*/", StringComparison.Ordinal);

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
