using System.Xml;
using System.Xml.Linq;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Reads a solution's project-reference graph from the project files as text, without MSBuild: the solution's projects and every
/// project they reference, transitively. Whatever the text cannot settle makes the graph unknown rather than smaller — a
/// reference or include through an MSBuild property other than the project's own directory, a wildcard reference, a missing
/// project, a <c>Directory.Build.props</c>/<c>.targets</c> or an imported file that adds project references, a project type
/// other than C#, F# or Visual Basic, a project outside the repository or inside a nested one — so a project the graph misses can
/// never go unbuilt.
/// </summary>
internal static class ProjectGraph
{
    private const string ProjectReferenceItem = "ProjectReference";
    private const string ImportElement = "Import";

    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly string[] InheritedFiles = ["Directory.Build.props", "Directory.Build.targets"];
    private static readonly string[] OwnDirectoryProperties = ["$(MSBuildThisFileDirectory)", "$(MSBuildProjectDirectory)/", "$(MSBuildProjectDirectory)\\", "$(MSBuildProjectDirectory)"];

    /// <summary>Items whose values are names (packages, namespaces, assemblies), never paths of files the project includes.</summary>
    private static readonly HashSet<string> NameItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "PackageReference", "PackageVersion", "GlobalPackageReference", "FrameworkReference", "Using", "InternalsVisibleTo",
        "Reference", "AssemblyAttribute", "SupportedPlatform", "ProjectCapability", "Service", "Folder",
    };

    /// <param name="describe">How a path is named in a problem (relative to the project root).</param>
    public static ProjectGraphRead Read(string solutionFile, IReadOnlyList<string> entries, string repositoryRoot, Func<string, string> describe)
    {
        Dictionary<string, GraphProject> projects = new(AffectedPaths.Comparer);
        Dictionary<string, string?> solutionEntries = new(AffectedPaths.Comparer);
        Queue<string> pending = new();
        foreach (string entry in entries)
        {
            string file = SolutionEntries.FullPath(solutionFile, entry);
            if (!ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                return ProjectGraphRead.Unknown($"{describe(file)} is not a C#, F# or Visual Basic project, so its references are not read");
            }

            if (solutionEntries.TryAdd(file, entry))
            {
                pending.Enqueue(file);
            }
        }

        bool inheritedTestHints = false;
        Dictionary<string, string?> inheritedText = new(AffectedPaths.Comparer);
        while (pending.TryDequeue(out string? file))
        {
            if (projects.ContainsKey(file))
            {
                continue;
            }

            if (Placement(file, repositoryRoot, describe) is string misplaced)
            {
                return ProjectGraphRead.Unknown(misplaced);
            }

            ProjectFileRead read = ReadProject(file, describe);
            if (read.Problem is string problem)
            {
                return ProjectGraphRead.Unknown(problem);
            }

            foreach (string inherited in InheritedAbove(file, inheritedText))
            {
                string text = inheritedText[inherited]!;
                if (text.Contains(ProjectReferenceItem, StringComparison.OrdinalIgnoreCase))
                {
                    return ProjectGraphRead.Unknown($"{describe(inherited)} adds project references, which the project files alone do not show");
                }

                inheritedTestHints |= ProjectFiles.MarksTests(text);
            }

            projects[file] = new GraphProject(file, solutionEntries.GetValueOrDefault(file), read.References, read.ExtraInputs, read.DeclaresTests);
            foreach (string reference in read.References)
            {
                pending.Enqueue(reference);
            }
        }

        // The solution's projects first, in its order, then the ones only referenced, in the order they were reached.
        return new ProjectGraphRead([.. projects.Values.OrderBy(project => project.SolutionEntry is null)], Problem: null, inheritedTestHints);
    }

    /// <summary>Why a project's changes would be invisible to the repository's git: outside it, or inside a nested repository.</summary>
    private static string? Placement(string file, string repositoryRoot, Func<string, string> describe)
    {
        if (!AffectedPaths.IsUnder(file, repositoryRoot))
        {
            return $"{describe(file)} lies outside this repository, whose git does not see its changes";
        }

        for (string? directory = Path.GetDirectoryName(file); directory is not null && AffectedPaths.IsUnder(directory, repositoryRoot) && !AffectedPaths.Same(directory, repositoryRoot); directory = Path.GetDirectoryName(directory))
        {
            if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")))
            {
                return $"{describe(file)} lies in a separate repository ({describe(directory)}), whose changes this repository's git does not see";
            }
        }

        return null;
    }

    private static ProjectFileRead ReadProject(string file, Func<string, string> describe)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            return ProjectFileRead.Failed($"{describe(file)} could not be read ({exception.Message})");
        }

        string directory = Path.GetDirectoryName(file)!;
        List<string> references = [];
        List<string> extraInputs = [];
        foreach (XElement element in document.Descendants())
        {
            string name = element.Name.LocalName;
            if (name == ImportElement)
            {
                if (ImportedInput(element, file, directory, describe, extraInputs) is string importProblem)
                {
                    return ProjectFileRead.Failed(importProblem);
                }

                continue;
            }

            if (element.Parent?.Name.LocalName != "ItemGroup" || NameItems.Contains(name) || element.Attribute("Include")?.Value is not string include)
            {
                continue;
            }

            foreach (string part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Resolve(part, directory) is not string path)
                {
                    return ProjectFileRead.Failed($"{describe(file)} includes '{part}', a path built from MSBuild properties or items the project files alone do not settle");
                }

                if (name == ProjectReferenceItem)
                {
                    if (HasWildcard(path) || !File.Exists(path))
                    {
                        return ProjectFileRead.Failed($"{describe(file)} references '{part}', which is not one existing project file");
                    }

                    references.Add(Path.GetFullPath(path));
                }
                else if (!AffectedPaths.IsUnder(path, directory))
                {
                    extraInputs.Add(HasWildcard(path) ? WildcardRoot(path) : Path.GetFullPath(path));
                }
            }
        }

        return new ProjectFileRead(references, extraInputs, ProjectFiles.MarksTests(document.ToString()), Problem: null);
    }

    /// <summary>
    /// An explicit import (not an SDK's): its file and directory become inputs of the project (a shared project's
    /// <c>.projitems</c> includes the files next to it), and one that adds project references makes the graph unknown.
    /// </summary>
    private static string? ImportedInput(XElement import, string file, string directory, Func<string, string> describe, List<string> extraInputs)
    {
        if (import.Attribute("Sdk") is not null || import.Attribute("Project")?.Value is not string project)
        {
            return null;
        }

        if (Resolve(project, directory) is not string path || HasWildcard(path))
        {
            return $"{describe(file)} imports '{project}', a path the project files alone do not settle";
        }

        if (!File.Exists(path))
        {
            // A conditional import of a file that is not there adds nothing.
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"{describe(path)}, imported by {describe(file)}, could not be read ({exception.Message})";
        }

        if (text.Contains(ProjectReferenceItem, StringComparison.OrdinalIgnoreCase))
        {
            return $"{describe(path)}, imported by {describe(file)}, adds project references, which the project files alone do not show";
        }

        extraInputs.Add(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return null;
    }

    /// <summary>The Directory.Build files MSBuild could import for the project, from its directory up to the file system root.</summary>
    private static IEnumerable<string> InheritedAbove(string file, Dictionary<string, string?> texts)
    {
        for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            foreach (string name in InheritedFiles)
            {
                string candidate = Path.Combine(directory, name);
                if (!texts.TryGetValue(candidate, out string? text))
                {
                    text = File.Exists(candidate) ? File.ReadAllText(candidate) : null;
                    texts[candidate] = text;
                }

                if (text is not null)
                {
                    yield return candidate;
                }
            }
        }
    }

    /// <summary>The value as a path, with the project's own directory properties resolved; null when another property or item remains.</summary>
    private static string? Resolve(string value, string directory)
    {
        string resolved = value;
        foreach (string property in OwnDirectoryProperties)
        {
            resolved = resolved.Replace(property, directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        if (resolved.Contains("$(", StringComparison.Ordinal) || resolved.Contains("@(", StringComparison.Ordinal) || resolved.Contains("%(", StringComparison.Ordinal))
        {
            return null;
        }

        string native = resolved.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(native) ? native : Path.Combine(directory, native));
    }

    private static bool HasWildcard(string path) => path.Contains('*', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal);

    /// <summary>The directory a wildcard include starts in (<c>../Shared/**/*.cs</c> → <c>../Shared</c>).</summary>
    private static string WildcardRoot(string path)
    {
        int wildcard = path.IndexOfAny(['*', '?']);
        return Path.GetDirectoryName(path[..wildcard] + "x")!;
    }

    private sealed record ProjectFileRead(IReadOnlyList<string> References, IReadOnlyList<string> ExtraInputs, bool DeclaresTests, string? Problem)
    {
        public static ProjectFileRead Failed(string problem) => new([], [], DeclaresTests: false, problem);
    }
}
