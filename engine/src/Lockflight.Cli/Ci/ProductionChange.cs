using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a pull request changes production code lines, the condition under which red/green, tests since red and spec/trace
/// apply at every class (risk routing §7): a changed line of a <c>production</c> file (C#, F#, Visual Basic, a <c>.cs</c> under no
/// project); a changed line of a <c>markup</c> file inside its C# logic (<see cref="RazorCodeBlocks"/>; a file whose text cannot be
/// read counts); or a changed line of a test project that a production project references, because that code ships. Project,
/// build-wide and config files do not count (a dependency bump needs no red); they raise their own routing signals.
/// </summary>
internal static class ProductionChange
{
    public static bool Of(ChangeAnalysis analysis)
    {
        HashSet<string> shipped = ShippedTestProjects(analysis);
        return analysis.Files.Any(file => file.Added.Count + file.Removed.Count > 0 && file.Role switch
        {
            FileRole.Production => true,
            FileRole.Markup => InLogic(file, analysis.ChangeSet),
            FileRole.Test => file.Project is string project && shipped.Contains(project),
            _ => false,
        });
    }

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
