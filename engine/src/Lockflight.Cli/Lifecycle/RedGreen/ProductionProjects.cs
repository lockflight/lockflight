using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Finds the production code under a root: every C# project that is not one of the test applications and not a shared
/// test-helper project (<see cref="TestHelperProjects"/>). Its assembly name is the project's <c>&lt;AssemblyName&gt;</c> when that is
/// a literal, else the project file's name; its types are read from the copies of those assemblies in the test applications'
/// output directories.
/// </summary>
internal static partial class ProductionProjects
{
    public static ProductionCode Find(string root, IReadOnlyList<TestApp> apps)
    {
        HashSet<string> testDirectories = new(apps.Select(app => Normalize(app.ProjectDirectory)), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> projects = new(StringComparer.OrdinalIgnoreCase);
        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Where(path => !IsBuildOutput(root, path)))
        {
            projects[Path.GetRelativePath(root, project).Replace('\\', '/')] = File.ReadAllText(project);
        }

        HashSet<string> appProjects = new(projects.Keys.Where(path => testDirectories.Contains(Normalize(Path.GetDirectoryName(Path.Combine(root, path))!))), StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> helpers = TestHelperProjects.Find(projects, appProjects);
        List<string> directories = [];
        HashSet<string> assemblies = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string project, string text) in projects.Where(entry => !appProjects.Contains(entry.Key) && !helpers.Contains(entry.Key)))
        {
            directories.Add(Path.GetDirectoryName(Path.Combine(root, project))!);
            Match name = AssemblyNamePattern().Match(text);
            assemblies.Add(name.Success && !name.Groups[1].Value.Contains('$', StringComparison.Ordinal) ? name.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(project));
        }

        return new ProductionCode(directories, assemblies, TypeNames(apps, assemblies));
    }

    private static HashSet<string> TypeNames(IReadOnlyList<TestApp> apps, HashSet<string> assemblies)
    {
        HashSet<string> types = new(StringComparer.Ordinal);
        HashSet<string> read = new(StringComparer.OrdinalIgnoreCase);
        foreach (string output in apps.Select(app => Path.GetDirectoryName(app.AssemblyPath)!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string assembly in assemblies)
            {
                string path = Path.Combine(output, assembly + ".dll");
                if (read.Contains(assembly) || !File.Exists(path))
                {
                    continue;
                }

                read.Add(assembly);
                AddTypes(path, types);
            }
        }

        return types;
    }

    private static void AddTypes(string assemblyPath, HashSet<string> types)
    {
        try
        {
            using PEReader pe = new(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                types.Add(FullName(reader, handle));
            }
        }
        catch (BadImageFormatException)
        {
            // Not a managed assembly: it declares no types a stack frame could name.
        }
    }

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{FullName(reader, declaring)}.{name}";
        }

        string space = reader.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    private static string Normalize(string directory) => Path.GetFullPath(directory).Replace('\\', '/').TrimEnd('/');

    private static bool IsBuildOutput(string root, string path)
    {
        string[] segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "node_modules" || segment.StartsWith('.'));
    }

    [GeneratedRegex(@"<AssemblyName>([^<]+)</AssemblyName>", RegexOptions.IgnoreCase)]
    private static partial Regex AssemblyNamePattern();
}
