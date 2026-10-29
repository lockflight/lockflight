using System.Xml;
using System.Xml.Linq;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// What one project's MSBuild files say, read as text: the project file, every file it imports (followed recursively), and the
/// <c>Directory.Build.props</c>/<c>.targets</c> and <c>Directory.Packages.props</c> above it with their imports. The project file
/// alone may add project references; an imported or inherited file that adds one makes the graph unknown. Files an item includes
/// from outside the project's directory, and the directories of imported files, are the project's extra inputs. A test marker
/// (<see cref="ProjectFiles.MarksTests"/>) in the project or its imports declares tests; one in an inherited file is a hint only.
/// The first thing the text cannot settle — a path through another MSBuild property, a file outside the repository, a file that
/// cannot be read — is the <see cref="Problem"/>.
/// </summary>
internal sealed class ProjectFileReading(string projectFile, string repositoryRoot, Func<string, string> describe)
{
    private const string ProjectReferenceItem = "ProjectReference";
    private const string ImportElement = "Import";

    private static readonly string[] InheritedFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];
    private static readonly string[] ThisFileDirectory = ["$(MSBuildThisFileDirectory)"];
    private static readonly string[] ProjectDirectory = ["$(MSBuildProjectDirectory)/", "$(MSBuildProjectDirectory)\\", "$(MSBuildProjectDirectory)"];

    /// <summary>Items whose values are names (packages, namespaces, assemblies), never paths of files the project includes.</summary>
    private static readonly HashSet<string> NameItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "PackageReference", "PackageVersion", "GlobalPackageReference", "FrameworkReference", "Using", "InternalsVisibleTo",
        "Reference", "AssemblyAttribute", "SupportedPlatform", "ProjectCapability", "Service", "Folder",
    };

    private readonly HashSet<string> visited = new(AffectedPaths.Comparer);

    public List<string> References { get; } = [];

    public List<string> ExtraInputs { get; } = [];

    public bool DeclaresTests { get; private set; }

    public bool InheritedTestHints { get; private set; }

    public string? Problem { get; private set; }

    private string ProjectDirectoryPath => Path.GetDirectoryName(projectFile)!;

    public static ProjectFileReading Of(string projectFile, string repositoryRoot, Func<string, string> describe)
    {
        ProjectFileReading reading = new(projectFile, repositoryRoot, describe);
        reading.Read(projectFile, MsBuildFileKind.Project);
        foreach (string inherited in InheritedAbove(projectFile))
        {
            if (reading.Problem is not null)
            {
                break;
            }

            reading.Read(inherited, MsBuildFileKind.Inherited);
        }

        return reading;
    }

    /// <summary>The Directory.Build and Directory.Packages files MSBuild could import for the project, from its directory up.</summary>
    private static IEnumerable<string> InheritedAbove(string file)
    {
        for (string? directory = Path.GetDirectoryName(file); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            foreach (string name in InheritedFiles)
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static bool HasWildcard(string path) => path.Contains('*', StringComparison.Ordinal) || path.Contains('?', StringComparison.Ordinal);

    /// <summary>The directory a wildcard include starts in (<c>../Shared/**/*.cs</c> → <c>../Shared</c>).</summary>
    private static string WildcardRoot(string path)
    {
        int wildcard = path.IndexOfAny(['*', '?']);
        return Path.GetDirectoryName(path[..wildcard] + "x")!;
    }

    private static string Replace(string value, IEnumerable<string> properties, string directory) =>
        properties.Aggregate(value, (resolved, property) => resolved.Replace(property, directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    private void Read(string path, MsBuildFileKind kind)
    {
        if (Problem is not null || !visited.Add(path))
        {
            return;
        }

        if (kind != MsBuildFileKind.Project && !AffectedPaths.IsUnder(path, repositoryRoot))
        {
            Problem = $"{describe(path)} lies outside this repository, whose git does not see its changes";
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException)
        {
            Problem = $"{describe(path)} could not be read ({exception.Message})";
            return;
        }

        if (ProjectFiles.MarksTests(document.ToString()))
        {
            DeclaresTests |= kind != MsBuildFileKind.Inherited;
            InheritedTestHints |= kind == MsBuildFileKind.Inherited;
        }

        string fileDirectory = Path.GetDirectoryName(path)!;
        foreach (XElement element in document.Descendants())
        {
            if (element.Name.LocalName == ImportElement)
            {
                ReadImport(element, path, fileDirectory, kind);
            }
            else if (element.Parent?.Name.LocalName == "ItemGroup" && !NameItems.Contains(element.Name.LocalName) && element.Attribute("Include")?.Value is string include)
            {
                ReadItem(element.Name.LocalName, include, path, fileDirectory, kind);
            }

            if (Problem is not null)
            {
                return;
            }
        }
    }

    /// <summary>An explicit import (not an SDK's) is read in turn; its directory is an input (a shared project's files sit beside its <c>.projitems</c>).</summary>
    private void ReadImport(XElement import, string path, string fileDirectory, MsBuildFileKind kind)
    {
        if (import.Attribute("Sdk") is not null || import.Attribute("Project")?.Value is not string project)
        {
            return;
        }

        if (kind == MsBuildFileKind.Inherited && project.Contains("FileAbove(", StringComparison.OrdinalIgnoreCase)
            && InheritedFiles.Any(name => project.Contains(name, StringComparison.OrdinalIgnoreCase)))
        {
            // The Directory.Build or Directory.Packages file further up, which is read as inherited in any case.
            return;
        }

        if (Resolve(project, fileDirectory, fileDirectory) is not string imported || HasWildcard(imported))
        {
            Problem = $"{describe(path)} imports '{project}', a path the project files alone do not settle";
            return;
        }

        if (File.Exists(imported))
        {
            // A conditional import of a file that is not there adds nothing.
            ExtraInputs.Add(Path.GetDirectoryName(imported)!);
            Read(imported, kind == MsBuildFileKind.Project ? MsBuildFileKind.Imported : kind);
        }
    }

    private void ReadItem(string item, string include, string path, string fileDirectory, MsBuildFileKind kind)
    {
        if (item == ProjectReferenceItem && kind != MsBuildFileKind.Project)
        {
            Problem = $"{describe(path)} adds project references, which the project files alone do not show";
            return;
        }

        foreach (string part in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Resolve(part, fileDirectory, ProjectDirectoryPath) is not string resolved)
            {
                Problem = $"{describe(path)} includes '{part}', a path built from MSBuild properties or items the project files alone do not settle";
                return;
            }

            if (item == ProjectReferenceItem)
            {
                if (HasWildcard(resolved) || !File.Exists(resolved))
                {
                    Problem = $"{describe(path)} references '{part}', which is not one existing project file";
                    return;
                }

                References.Add(resolved);
            }
            else if (!AffectedPaths.IsUnder(resolved, ProjectDirectoryPath))
            {
                ExtraInputs.Add(HasWildcard(resolved) ? WildcardRoot(resolved) : resolved);
            }
        }
    }

    /// <summary>
    /// The value as a full path: <c>$(MSBuildThisFileDirectory)</c> is the file's directory, <c>$(MSBuildProjectDirectory)</c> the
    /// project's, and a relative path is relative to <paramref name="relativeTo"/> (the importing file for an import, the project for
    /// an item, as MSBuild resolves them). Null when another property or an item transform remains.
    /// </summary>
    private static string? Resolve(string value, string fileDirectory, string relativeTo, string projectDirectory)
    {
        string resolved = Replace(Replace(value, ThisFileDirectory, fileDirectory), ProjectDirectory, projectDirectory);
        if (resolved.Contains("$(", StringComparison.Ordinal) || resolved.Contains("@(", StringComparison.Ordinal) || resolved.Contains("%(", StringComparison.Ordinal))
        {
            return null;
        }

        string native = resolved.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(native) ? native : Path.Combine(relativeTo, native));
    }

    private string? Resolve(string value, string fileDirectory, string relativeTo) => Resolve(value, fileDirectory, relativeTo, ProjectDirectoryPath);
}
