using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Production code a project compiles from a file that is not C# by its name: a changed line of a production project or build-wide
/// MSBuild file (<c>*.props</c>, <c>*.targets</c>, not one that only test projects sit under) that includes or removes a
/// <c>&lt;Compile&gt;</c> item, switches the default items, or sets the default source extension; or a changed file of any other
/// role whose path a <c>&lt;Compile Include="…"&gt;</c> at head names (a path or a glob, read relative to the file that declares it
/// and to the changed file's project), or whose extension a <c>&lt;DefaultLanguageSourceExtension&gt;</c> at head names for its
/// folder. Without this, <c>Pricing.logic</c> with <c>&lt;Compile Include="**/*.logic" /&gt;</c> would be production code that needs
/// no red. Any other property in an include cannot be evaluated without the build and is left out.
/// </summary>
internal static partial class CompiledFiles
{
    private static readonly FileRole[] CountedElsewhere = [FileRole.Production, FileRole.Markup, FileRole.Test];

    public static bool Changed(ChangeAnalysis analysis)
    {
        if (analysis.Files.Any(file => CompileItemsChanged(analysis, file)))
        {
            return true;
        }

        RoutedFile[] candidates = [.. analysis.Files.Where(file => !CountedElsewhere.Contains(file.Role) && (file.Change.Binary || file.Added.Count + file.Removed.Count > 0))];
        if (candidates.Length == 0)
        {
            return false;
        }

        List<CompilePattern> patterns = Patterns(analysis.ChangeSet.Head);
        return candidates.Any(file => Named(file, patterns));
    }

    private static bool CompileItemsChanged(ChangeAnalysis analysis, RoutedFile file)
    {
        bool test = (!file.Change.ExistsAtHead || analysis.HeadRoles.IsTestProject(file.Path)) && (!file.Change.ExistsAtBase || analysis.BaseRoles.IsTestProject(file.Path));
        bool msbuild = ProjectIndex.IsProjectFile(file.Path) ? !test : file.Role == FileRole.BuildWide && !OnlyTestProjectsUnder(analysis, file.Path);
        return msbuild && file.Added.Concat(file.Removed).Any(line => CompileLine().IsMatch(line.Text));
    }

    /// <summary>Whether every project under a build-wide file's folder at head is a test project (and there is one).</summary>
    private static bool OnlyTestProjectsUnder(ChangeAnalysis analysis, string path)
    {
        string directory = ProjectIndex.DirectoryOf(path);
        string[] projects = [.. analysis.ChangeSet.Head.Paths.Where(candidate => ProjectIndex.IsProjectFile(candidate)
            && (directory.Length == 0 || candidate.StartsWith(directory + "/", StringComparison.Ordinal)))];
        return projects.Length > 0 && projects.All(analysis.HeadRoles.IsTestProject);
    }

    /// <summary>Every include and default source extension at head, read once for all the changed files.</summary>
    private static List<CompilePattern> Patterns(RevisionSnapshot head)
    {
        List<CompilePattern> patterns = [];
        foreach (string declaring in head.Paths.Where(path => ProjectIndex.IsProjectFile(path) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
        {
            if (head.TextOf(declaring) is not string text)
            {
                continue;
            }

            string directory = ProjectIndex.DirectoryOf(declaring);
            foreach (Match include in CompileInclude().Matches(text))
            {
                foreach (string written in include.Groups["path"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string pattern = ThisDirectory().Replace(written.Replace('\\', '/'), string.Empty).TrimStart('/');
                    if (!pattern.Contains("$(", StringComparison.Ordinal))
                    {
                        patterns.Add(new CompilePattern(directory, pattern, OfEachProject: true));
                    }
                }
            }

            foreach (Match extension in SourceExtension().Matches(text))
            {
                patterns.Add(new CompilePattern(directory, $"**/*{extension.Groups["extension"].Value.Trim()}", OfEachProject: false));
            }
        }

        return patterns;
    }

    private static bool Named(RoutedFile file, List<CompilePattern> patterns)
    {
        string? project = file.Project is string projectFile ? ProjectIndex.DirectoryOf(projectFile) : null;
        return patterns.Any(pattern => PathRules.Matches(Combine(pattern.Directory, pattern.Glob), file.Path)
            || (pattern.OfEachProject && project is not null && PathRules.Matches(Combine(project, pattern.Glob), file.Path)));
    }

    private static string Combine(string directory, string pattern)
    {
        List<string> segments = [.. directory.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (string segment in pattern.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".." && segments.Count > 0 && segments[^1] != "**")
            {
                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>A line that changes what compiles: a <c>Compile</c> item that is not only an <c>Update</c> of metadata, or a switch.</summary>
    [GeneratedRegex(@"<Compile\b(?![^>]*\bUpdate\s*=)|<(?:EnableDefaultCompileItems|EnableDefaultItems|DefaultItemExcludes|DefaultLanguageSourceExtension|CompileDependsOn)\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompileLine();

    [GeneratedRegex(@"<Compile\b[^>]*?\bInclude\s*=\s*(?:""(?<path>[^""]*)""|'(?<path>[^']*)')", RegexOptions.CultureInvariant)]
    private static partial Regex CompileInclude();

    [GeneratedRegex(@"<DefaultLanguageSourceExtension>(?<extension>[^<$]+)</DefaultLanguageSourceExtension>", RegexOptions.CultureInvariant)]
    private static partial Regex SourceExtension();

    [GeneratedRegex(@"\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ThisDirectory();
}
