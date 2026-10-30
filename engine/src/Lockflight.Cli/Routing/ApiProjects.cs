using System.Text.RegularExpressions;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Whether a production project's public surface is an API: its text, or a <c>Directory.Build.props</c> above it, sets
/// <c>&lt;IsPackable&gt;true</c>, a <c>&lt;PackageId&gt;</c> or <c>&lt;GeneratePackageOnBuild&gt;true</c> (a property under a
/// <c>Condition</c> counts as set); or it has a <c>PublicAPI.Shipped.txt</c>; or it matches <c>routing.apiProjects</c>. It is one when
/// either side says so, so dropping <c>&lt;IsPackable&gt;</c> in the same pull request does not hide its API changes. In an application project nearly every class is <c>public</c>, so its API is
/// its endpoints, not its declarations.
/// </summary>
internal static partial class ApiProjects
{
    private const string ShippedApiFile = "PublicAPI.Shipped.txt";

    /// <summary>Whether the project (its head path) is an API project at base (followed through a move) or at head: either side counts.</summary>
    public static bool IsApiProject(ChangeAnalysis analysis, RoutingPolicy policy, string projectFile)
    {
        string basePath = analysis.Moves.FirstOrDefault(move => move.NewPath == projectFile)?.OldPath ?? projectFile;
        return (analysis.ChangeSet.Base.Contains(basePath) && IsApiProjectOn(analysis.ChangeSet.Base, analysis.BaseRoles, policy, basePath))
            || (analysis.ChangeSet.Head.Contains(projectFile) && IsApiProjectOn(analysis.ChangeSet.Head, analysis.HeadRoles, policy, projectFile));
    }

    private static bool IsApiProjectOn(RevisionSnapshot side, FileRoles roles, RoutingPolicy policy, string projectFile)
    {
        string directory = ProjectIndex.DirectoryOf(projectFile);
        if (policy.Settings.ApiProjects.Any(glob => PathRules.Matches(glob, projectFile) || PathRules.Matches(glob, directory) || PathRules.Matches(glob, directory + "/")))
        {
            return true;
        }

        if (side.Contains(directory.Length == 0 ? ShippedApiFile : $"{directory}/{ShippedApiFile}"))
        {
            return true;
        }

        return new[] { projectFile }.Concat(roles.Projects.PropsChain(projectFile))
            .Any(path => side.TextOf(path) is string text && Packable().IsMatch(ProjectText.WithoutComments(text)));
    }

    [GeneratedRegex(@"<IsPackable\b[^>]*>\s*true\s*</IsPackable>|<PackageId\b[^>]*>\s*[^<\s]|<GeneratePackageOnBuild\b[^>]*>\s*true\s*</GeneratePackageOnBuild>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Packable();
}
