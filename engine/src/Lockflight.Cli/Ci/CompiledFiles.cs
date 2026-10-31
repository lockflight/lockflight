using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Production code a project compiles from a file that is not C# by its name: a changed line of a production project or build-wide
/// MSBuild file (<c>*.props</c>, <c>*.targets</c>) that adds, removes or edits a <c>&lt;Compile&gt;</c> item or switches the default
/// compile items; or a changed file of any other role whose path a <c>&lt;Compile Include="…"&gt;</c> at head names (a path or a glob,
/// read relative to the file that declares it and to the changed file's project). Without this, <c>Pricing.logic</c> with
/// <c>&lt;Compile Include="**/*.logic" /&gt;</c> would be production code that needs no red. A property in an include cannot be
/// evaluated without the build and is left out.
/// </summary>
internal static partial class CompiledFiles
{
    private static readonly FileRole[] CountedElsewhere = [FileRole.Production, FileRole.Markup, FileRole.Test];

    public static bool Changed(ChangeAnalysis analysis) =>
        analysis.Files.Any(file => CompileItemsChanged(analysis, file)) || analysis.Files.Any(file => NamedByAnInclude(analysis, file));

    private static bool CompileItemsChanged(ChangeAnalysis analysis, RoutedFile file)
    {
        bool test = (!file.Change.ExistsAtHead || analysis.HeadRoles.IsTestProject(file.Path)) && (!file.Change.ExistsAtBase || analysis.BaseRoles.IsTestProject(file.Path));
        bool msbuild = ProjectIndex.IsProjectFile(file.Path) ? !test : file.Role == FileRole.BuildWide;
        return msbuild && file.Added.Concat(file.Removed).Any(line => CompileLine().IsMatch(line.Text));
    }

    private static bool NamedByAnInclude(ChangeAnalysis analysis, RoutedFile file)
    {
        if (CountedElsewhere.Contains(file.Role) || (file.Added.Count + file.Removed.Count == 0 && !file.Change.Binary))
        {
            return false;
        }

        RevisionSnapshot head = analysis.ChangeSet.Head;
        string[] directories = [.. new[] { file.Project is string project ? ProjectIndex.DirectoryOf(project) : null }.OfType<string>()];
        foreach (string declaring in head.Paths.Where(path => ProjectIndex.IsProjectFile(path) || path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
        {
            if (head.TextOf(declaring) is not string text)
            {
                continue;
            }

            foreach (Match include in CompileInclude().Matches(text))
            {
                foreach (string written in include.Groups["path"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string pattern = ThisDirectory().Replace(written.Replace('\\', '/'), string.Empty).TrimStart('/');
                    if (!pattern.Contains("$(", StringComparison.Ordinal)
                        && directories.Append(ProjectIndex.DirectoryOf(declaring)).Any(directory => PathRules.Matches(Combine(directory, pattern), file.Path)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
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

    [GeneratedRegex(@"<Compile\b|<EnableDefaultCompileItems\b|<DefaultItemExcludes\b|<CompileDependsOn\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompileLine();

    [GeneratedRegex(@"<Compile\b[^>]*?\bInclude\s*=\s*""(?<path>[^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex CompileInclude();

    [GeneratedRegex(@"\$\((MSBuildThisFileDirectory|MSBuildProjectDirectory)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ThisDirectory();
}
