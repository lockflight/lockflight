using System.Text.RegularExpressions;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The public API files of the Roslyn public API analyzer (<c>PublicAPI.Shipped.txt</c>, <c>PublicAPI.Unshipped.txt</c>, also in
/// per-framework folders) and whether a project is held to them: it has a shipped file at base, and its text or a
/// <c>Directory.Build.props</c> above it references <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c>, so the build gate's "no new
/// warnings" fails a public surface change the files do not record.
/// </summary>
internal static partial class PublicApiFiles
{
    public const string ShippedName = "PublicAPI.Shipped.txt";
    public const string UnshippedName = "PublicAPI.Unshipped.txt";

    public static bool IsShipped(string path) => ProjectIndex.FileName(path).Equals(ShippedName, StringComparison.OrdinalIgnoreCase);

    public static bool IsUnshipped(string path) => ProjectIndex.FileName(path).Equals(UnshippedName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the project directory (or a folder below it) holds a shipped file at base.</summary>
    public static bool HasShippedAtBase(ChangeAnalysis analysis, string projectFile)
    {
        string directory = ProjectIndex.DirectoryOf(projectFile);
        string prefix = directory.Length == 0 ? string.Empty : directory + "/";
        return analysis.ChangeSet.Base.Paths.Any(path => IsShipped(path) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && analysis.BaseRoles.Projects.OwningProjects(path).Contains(projectFile, StringComparer.Ordinal));
    }

    /// <summary>Whether the project is held to its public API files: a shipped file at base and the analyzer referenced at base.</summary>
    public static bool Enforced(ChangeAnalysis analysis, string projectFile) =>
        analysis.ChangeSet.Base.Contains(projectFile)
        && HasShippedAtBase(analysis, projectFile)
        && new[] { projectFile }.Concat(analysis.BaseRoles.Projects.PropsChain(projectFile))
            .Any(path => analysis.ChangeSet.Base.TextOf(path) is string text && Analyzer().IsMatch(ProjectText.WithoutComments(text)));

    [GeneratedRegex(@"<(?:PackageReference|GlobalPackageReference)\b[^>]*\bInclude\s*=\s*""\s*Microsoft\.CodeAnalysis\.PublicApiAnalyzers\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Analyzer();
}
