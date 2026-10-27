using System.Text;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Reads test projects from their files, without building: which projects under a root are xUnit test projects, their
/// <see cref="TestProjectProfile"/>, the using directives their tests use, and one sample test. Everything is bounded, so a
/// large repository cannot blow up the review prompt.
/// </summary>
internal static partial class TestProjectProfiles
{
    public const string GeneratedFileName = "LockflightReproductions.g.cs";

    private const int MaxProjects = 20;
    private const int MaxUsings = 60;
    private const int MaxSampleCharacters = 2_500;

    /// <summary>The xUnit test projects under the root, in path order (at most <see cref="MaxProjects"/>).</summary>
    public static IReadOnlyList<string> Find(string root) =>
        [.. ProjectFiles(root).Where(project => IsTestProject(File.ReadAllText(project))).Take(MaxProjects)];

    /// <summary>The project file of a project directory: the one named after the assembly when there are several.</summary>
    public static string? ProjectFile(string projectDirectory, string? assemblyName = null)
    {
        string[] projects = Directory.Exists(projectDirectory) ? [.. Directory.EnumerateFiles(projectDirectory, "*.csproj").Order(StringComparer.Ordinal)] : [];
        return projects.FirstOrDefault(project => string.Equals(Path.GetFileNameWithoutExtension(project), assemblyName, StringComparison.OrdinalIgnoreCase))
            ?? projects.FirstOrDefault();
    }

    public static TestProjectProfile Describe(string projectFile, string root, bool? xunitV2 = null)
    {
        string text = File.ReadAllText(projectFile);
        string directory = Path.GetDirectoryName(projectFile)!;
        bool v2 = xunitV2 ?? !text.Contains("xunit.v3", StringComparison.OrdinalIgnoreCase);
        return new TestProjectProfile(
            Path.GetFileNameWithoutExtension(projectFile),
            Relative(root, projectFile),
            TargetFrameworks(text, directory, root),
            v2 ? "xUnit v2 (VSTest)" : "xUnit v3",
            [.. ProjectReferences(projectFile).Select(reference => Relative(root, reference))],
            [.. PackageReference().Matches(text).Select(match => match.Groups["name"].Value).Distinct(StringComparer.OrdinalIgnoreCase)],
            Usings(directory));
    }

    /// <summary>The projects a project file references, as full paths (references built from MSBuild properties are left out).</summary>
    public static IReadOnlyList<string> ProjectReferences(string projectFile)
    {
        string directory = Path.GetDirectoryName(projectFile)!;
        return [.. ProjectReference().Matches(File.ReadAllText(projectFile))
            .Select(match => match.Groups["path"].Value)
            .Where(path => !path.Contains("$(", StringComparison.Ordinal))
            .Select(path => Path.GetFullPath(Path.Combine(directory, path.Replace('\\', Path.DirectorySeparatorChar))))];
    }

    /// <summary>
    /// The using directives the project's C# files start with (those before any namespace, so a relative <c>using</c> inside a
    /// namespace block is not taken out of context), plus <c>using Xunit;</c>; an alias defined two ways keeps its first meaning.
    /// </summary>
    public static IReadOnlyList<string> Usings(string projectDirectory)
    {
        List<string> usings = [];
        HashSet<string> aliases = new(StringComparer.Ordinal);
        foreach (string line in SourceFiles(projectDirectory).SelectMany(LeadingUsings).Append("using Xunit;").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Match alias = UsingAlias().Match(line);
            if (alias.Success && !aliases.Add(alias.Groups["alias"].Value))
            {
                continue;
            }

            usings.Add(line);
        }

        return [.. usings.Take(MaxUsings)];
    }

    /// <summary>The first <c>[Fact]</c> of the project with its file's usings, namespace and class line; null when there is none.</summary>
    public static string? SampleTest(string projectDirectory, string root)
    {
        foreach (string file in SourceFiles(projectDirectory))
        {
            string source = File.ReadAllText(file);
            string code = CSharpMask.Mask(source);
            int fact = FactAttribute().Match(code) is { Success: true } match ? match.Index : -1;
            if (fact < 0)
            {
                continue;
            }

            StringBuilder sample = new();
            sample.Append("// ").Append(Relative(root, file)).Append('\n');
            foreach (string line in LeadingUsings(file))
            {
                sample.Append(line).Append('\n');
            }

            foreach (Match declaration in Declaration().Matches(code[..fact]))
            {
                sample.Append(source.Substring(declaration.Index, declaration.Length).Trim()).Append('\n');
            }

            int end = MethodEnd(code, fact);
            sample.Append(source[fact..end].Trim()).Append('\n');
            string text = sample.ToString();
            return text.Length > MaxSampleCharacters ? text[..MaxSampleCharacters] + "\n// … (truncated)" : text;
        }

        return null;
    }

    public static bool IsTestProject(string projectText) =>
        TestPackage().IsMatch(projectText) || projectText.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase);

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> ProjectFiles(string root) =>
        Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Where(path => !IsIgnored(root, path)).Order(StringComparer.Ordinal);

    private static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsIgnored(projectDirectory, file) && !file.EndsWith(GeneratedFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static IEnumerable<string> LeadingUsings(string file)
    {
        foreach (string raw in File.ReadLines(file))
        {
            string line = raw.Trim();
            if (UsingDirective().IsMatch(line))
            {
                yield return line;
            }
            else if (line.StartsWith("namespace", StringComparison.Ordinal) || Declaration().IsMatch(line))
            {
                yield break;
            }
        }
    }

    /// <summary>The position after the body of the method whose attribute starts at <paramref name="start"/> (a block or an expression body).</summary>
    private static int MethodEnd(string code, int start)
    {
        int arrow = code.IndexOf("=>", start, StringComparison.Ordinal);
        int brace = code.IndexOf('{', start);
        if (brace < 0 && arrow < 0)
        {
            return code.Length;
        }

        if (arrow >= 0 && (brace < 0 || arrow < brace) && !code[start..arrow].Contains(';', StringComparison.Ordinal))
        {
            int semicolon = code.IndexOf(';', arrow);
            return semicolon < 0 ? code.Length : semicolon + 1;
        }

        int depth = 0;
        for (int position = brace; position < code.Length; position++)
        {
            if (code[position] == '{')
            {
                depth++;
            }
            else if (code[position] == '}' && --depth == 0)
            {
                return position + 1;
            }
        }

        return code.Length;
    }

    private static string TargetFrameworks(string projectText, string directory, string root)
    {
        if (TargetFramework().Match(projectText) is { Success: true } own)
        {
            return own.Groups["value"].Value.Trim();
        }

        // Inherited from a Directory.Build.props between the project and the root.
        for (DirectoryInfo? folder = new(directory); folder is not null && folder.FullName.Length >= Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Length; folder = folder.Parent)
        {
            string props = Path.Combine(folder.FullName, "Directory.Build.props");
            if (File.Exists(props) && TargetFramework().Match(File.ReadAllText(props)) is { Success: true } inherited)
            {
                return inherited.Groups["value"].Value.Trim();
            }
        }

        return "unknown";
    }

    private static bool IsIgnored(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => segment is "bin" or "obj" or "node_modules" || segment.StartsWith('.'));

    [GeneratedRegex(@"^using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>, ]+)?\s*;$")]
    private static partial Regex UsingDirective();

    [GeneratedRegex(@"^using\s+(?<alias>\w+)\s*=")]
    private static partial Regex UsingAlias();

    [GeneratedRegex(@"<ProjectReference\s+Include\s*=\s*""(?<path>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectReference();

    [GeneratedRegex(@"<PackageReference\s+Include\s*=\s*""(?<name>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex PackageReference();

    [GeneratedRegex(@"<PackageReference\s+Include\s*=\s*""xunit(\.v3(\.mtp-v[12]|\.core)?)?""", RegexOptions.IgnoreCase)]
    private static partial Regex TestPackage();

    [GeneratedRegex(@"<TargetFrameworks?>(?<value>[^<]+)</TargetFrameworks?>", RegexOptions.IgnoreCase)]
    private static partial Regex TargetFramework();

    [GeneratedRegex(@"\[\s*Fact\b")]
    private static partial Regex FactAttribute();

    [GeneratedRegex(@"^[ \t]*(?:namespace\s+[\w.]+;?|(?:(?:public|internal|sealed|static|abstract|partial|file)\s+)*(?:class|record|struct)\s+\w+[^\n{;]*)", RegexOptions.Multiline)]
    private static partial Regex Declaration();
}
