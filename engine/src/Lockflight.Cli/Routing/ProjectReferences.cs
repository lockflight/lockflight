using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads the <c>&lt;ProjectReference Include="…"&gt;</c> items of a whole project file (an element may span lines, several may share
/// one, an <c>Include</c> may list several paths with <c>;</c>; XML comments are ignored), each resolved from the project's directory
/// to a path relative to the repository root. <c>$(MSBuildThisFileDirectory)</c> and <c>$(MSBuildProjectDirectory)</c> are the
/// project's directory; any other property cannot be resolved without evaluating the build, and the reference is returned
/// unresolved.
/// </summary>
internal static partial class ProjectReferences
{
    public static IReadOnlyList<ProjectReference> Of(string projectText, string projectFile)
    {
        string text = CommentToSpaces(projectText);
        string directory = ProjectIndex.DirectoryOf(projectFile);
        List<ProjectReference> references = [];
        foreach (Match include in Include().Matches(text))
        {
            int line = 1 + text.AsSpan(0, include.Index).Count('\n');
            foreach (string written in include.Groups["path"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string value = DirectoryProperty().Replace(written.Replace('\\', '/'), string.Empty).TrimStart('/');
                references.Add(new ProjectReference(written, value.Contains("$(", StringComparison.Ordinal) ? null : Resolve(directory, value), line));
            }
        }

        return references;
    }

    private static string? Resolve(string directory, string relative)
    {
        List<string> segments = [.. directory.Split('/', StringSplitOptions.RemoveEmptyEntries)];
        foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }

    /// <summary>Blanks XML comments, keeping line breaks so line numbers stay right.</summary>
    private static string CommentToSpaces(string text) =>
        Comment().Replace(text, match => new string([.. match.Value.Select(character => character == '\n' ? '\n' : ' ')]));

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<ProjectReference\b[^>]*?\bInclude\s*=\s*""(?<path>[^""]*)""", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Include();

    [GeneratedRegex(@"\$\((?:MSBuildThisFileDirectory|MSBuildProjectDirectory)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DirectoryProperty();
}
