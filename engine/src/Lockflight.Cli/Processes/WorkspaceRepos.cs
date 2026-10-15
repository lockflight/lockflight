using Lockflight.Cli.Config;

namespace Lockflight.Cli.Processes;

/// <summary>
/// The git working trees a project's gates must see: the project root, then every unit with its own <c>.git</c>. A
/// workspace shell ignores its child repositories, so changes there are invisible to the shell's git and must be asked of each
/// child; a single repository is just the first entry.
/// </summary>
internal static class WorkspaceRepos
{
    public static IReadOnlyList<RepoRoot> Of(string projectRoot)
    {
        List<RepoRoot> repos = [new(projectRoot, string.Empty)];
        if (ManifestLocator.Discover(projectRoot).Manifest is Manifest manifest && SameDirectory(manifest.Root, projectRoot))
        {
            repos.AddRange(manifest.Units
                .Select(unit => new RepoRoot(Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar)), unit.Path + "/"))
                .Where(repo => Directory.Exists(Path.Combine(repo.Directory, ".git")) || File.Exists(Path.Combine(repo.Directory, ".git"))));
        }

        return repos;
    }

    /// <summary>Whether a path the project root's git lists is (inside) a child repository, which that child reports instead.</summary>
    public static bool InChildRepository(IReadOnlyList<RepoRoot> repos, string relativePath) =>
        repos.Any(repo => repo.Prefix.Length > 0
            && (relativePath.StartsWith(repo.Prefix, StringComparison.OrdinalIgnoreCase) || relativePath.Equals(repo.Prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)));

    private static bool SameDirectory(string first, string second) =>
        Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal);
}
