namespace Lockflight.Cli.Routing;

/// <summary>
/// Groups the declarations of a change's C# files by project, base side under the project's base path (followed through a project
/// move), head side under its head path, and diffs them as multisets by <see cref="CSharpDeclaration.Key"/>. Declarations of
/// files outside the change are the same on both sides, so the changed files are enough. Only files whose role on that side is in
/// <c>roles</c> are read.
/// </summary>
internal static class DeclarationSets
{
    public static IReadOnlyList<ProjectDeclarations> Of(ChangeAnalysis analysis, IReadOnlyCollection<FileRole> roles)
    {
        Dictionary<string, string> projectMoves = analysis.Moves
            .Where(move => ProjectIndex.IsProjectFile(move.OldPath))
            .ToDictionary(move => move.OldPath, move => move.NewPath, StringComparer.Ordinal);
        SortedDictionary<string, (List<PlacedDeclaration> Base, List<PlacedDeclaration> Head)> byProject = new(StringComparer.Ordinal);
        foreach (RoutedFile file in analysis.Files.Where(file => ChangeAnalyzer.IsCSharp(file.Path)))
        {
            if (file.AtBase is FileRoleAt atBase && roles.Contains(atBase.Role) && analysis.ChangeSet.Base.TextOf(file.Path) is string baseText)
            {
                string project = atBase.Project is string path ? projectMoves.GetValueOrDefault(path, path) : string.Empty;
                Group(byProject, project).Base.AddRange(CSharpDeclarations.Extract(baseText).Select(declaration => new PlacedDeclaration(declaration, file.Path)));
            }

            if (file.AtHead is FileRoleAt atHead && roles.Contains(atHead.Role) && analysis.ChangeSet.Head.TextOf(file.Path) is string headText)
            {
                Group(byProject, atHead.Project ?? string.Empty).Head.AddRange(CSharpDeclarations.Extract(headText).Select(declaration => new PlacedDeclaration(declaration, file.Path)));
            }
        }

        return [.. byProject.Select(entry => new ProjectDeclarations(
            entry.Key.Length == 0 ? null : entry.Key,
            entry.Value.Base,
            entry.Value.Head,
            Minus(entry.Value.Head, entry.Value.Base),
            Minus(entry.Value.Base, entry.Value.Head)))];
    }

    private static (List<PlacedDeclaration> Base, List<PlacedDeclaration> Head) Group(
        SortedDictionary<string, (List<PlacedDeclaration> Base, List<PlacedDeclaration> Head)> byProject, string project)
    {
        if (!byProject.TryGetValue(project, out (List<PlacedDeclaration> Base, List<PlacedDeclaration> Head) group))
        {
            group = ([], []);
            byProject[project] = group;
        }

        return group;
    }

    private static List<PlacedDeclaration> Minus(List<PlacedDeclaration> from, List<PlacedDeclaration> other)
    {
        Dictionary<string, int> available = new(StringComparer.Ordinal);
        foreach (PlacedDeclaration placed in other)
        {
            available[placed.Declaration.Key] = available.GetValueOrDefault(placed.Declaration.Key) + 1;
        }

        List<PlacedDeclaration> missing = [];
        foreach (PlacedDeclaration placed in from)
        {
            if (available.GetValueOrDefault(placed.Declaration.Key) > 0)
            {
                available[placed.Declaration.Key]--;
            }
            else
            {
                missing.Add(placed);
            }
        }

        return missing;
    }
}
