using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Ef;

/// <summary>
/// The DbContexts with migrations in a unit, found from their model snapshots (<c>*ModelSnapshot.cs</c> in a <c>Migrations</c>
/// folder or below it, the file EF Core writes with every migration): the context from its <c>[DbContext(typeof(…))]</c>, the
/// project from the nearest project file above it. The startup project is the project itself when it references
/// <c>Microsoft.EntityFrameworkCore.Design</c>, else the first project of the unit (by path) that references both that package and
/// this project. <c>bin/</c>, <c>obj/</c> and hidden directories are not searched. A light reading of the project files, not
/// MSBuild: a reference added through an imported file is not seen.
/// </summary>
internal static partial class EfContexts
{
    private const string DesignPackage = "Microsoft.EntityFrameworkCore.Design";
    private const string SnapshotSuffix = "ModelSnapshot.cs";
    private const int MaxReferenceDepth = 32;

    private static readonly string[] SkippedDirectories = ["bin", "obj", "node_modules"];

    public static IReadOnlyList<EfContext> Find(string projectRoot, string unitDirectory)
    {
        string root = Path.GetFullPath(projectRoot);
        string[] projectFiles = [.. Files(unitDirectory, "*.csproj").Order(StringComparer.Ordinal)];
        List<EfContext> contexts = [];
        foreach (string snapshot in Files(unitDirectory, "*" + SnapshotSuffix).Order(StringComparer.Ordinal))
        {
            if (!Path.GetDirectoryName(snapshot)!.Split(Path.DirectorySeparatorChar).Contains("Migrations", StringComparer.Ordinal)
                || ContextPattern().Match(File.ReadAllText(snapshot)) is not { Success: true } context
                || OwningProject(snapshot, unitDirectory) is not string project)
            {
                continue;
            }

            string name = context.Groups["name"].Value.Split('.')[^1];
            string? startup = References(project, DesignPackage) ? null
                : projectFiles.FirstOrDefault(candidate => candidate != project && References(candidate, DesignPackage) && ReferencedProjects(candidate).Contains(project));
            string[] modelDirectories = [.. new[] { project }.Concat(TransitiveReferences(project)).Select(file => Relative(root, Path.GetDirectoryName(file)!)).Distinct(StringComparer.Ordinal)];
            EfContext found = new(Relative(root, project), Relative(root, Path.GetDirectoryName(project)!), name, startup is null ? null : Relative(root, startup), modelDirectories);
            if (!contexts.Any(existing => existing.Project == found.Project && existing.Context == found.Context))
            {
                contexts.Add(found);
            }
        }

        return contexts;
    }

    private static IEnumerable<string> Files(string directory, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(directory, pattern))
        {
            yield return Path.GetFullPath(file);
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);
            if (name.StartsWith('.') || SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) || new DirectoryInfo(child).LinkTarget is not null)
            {
                continue;
            }

            foreach (string file in Files(child, pattern))
            {
                yield return file;
            }
        }
    }

    private static string? OwningProject(string file, string unitDirectory)
    {
        string stop = Path.GetFullPath(unitDirectory).TrimEnd(Path.DirectorySeparatorChar);
        for (DirectoryInfo? directory = new FileInfo(file).Directory; directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.csproj").Order().FirstOrDefault() is FileInfo project)
            {
                return project.FullName;
            }

            if (directory.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(stop, StringComparison.Ordinal))
            {
                break;
            }
        }

        return null;
    }

    private static bool References(string projectFile, string package) =>
        PackagePattern().Matches(ReadProject(projectFile)).Any(match => match.Groups["id"].Value.Equals(package, StringComparison.OrdinalIgnoreCase));

    /// <summary>The project files a project references directly, as full paths.</summary>
    private static HashSet<string> ReferencedProjects(string projectFile)
    {
        string directory = Path.GetDirectoryName(projectFile)!;
        return [.. ProjectReferencePattern().Matches(ReadProject(projectFile))
            .Select(match => Path.GetFullPath(Path.Combine(directory, match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar))))];
    }

    private static HashSet<string> TransitiveReferences(string projectFile)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Queue<(string File, int Depth)> pending = new([(projectFile, 0)]);
        while (pending.TryDequeue(out (string File, int Depth) next))
        {
            if (next.Depth >= MaxReferenceDepth)
            {
                continue;
            }

            foreach (string referenced in ReferencedProjects(next.File).Where(File.Exists))
            {
                if (seen.Add(referenced))
                {
                    pending.Enqueue((referenced, next.Depth + 1));
                }
            }
        }

        return seen;
    }

    private static string ReadProject(string projectFile) => File.Exists(projectFile) ? XmlComment().Replace(File.ReadAllText(projectFile), string.Empty) : string.Empty;

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    [GeneratedRegex(@"\[\s*DbContext\s*\(\s*typeof\s*\(\s*(?<name>[\w.]+)\s*\)\s*\)\s*\]")]
    private static partial Regex ContextPattern();

    [GeneratedRegex(@"<PackageReference\s[^>]*\bInclude\s*=\s*""(?<id>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"<ProjectReference\s[^>]*\bInclude\s*=\s*""(?<path>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectReferencePattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlComment();
}
