using Lockflight.Cli.Config;

namespace Lockflight.Cli.Gates.WindowsTargets;

/// <summary>
/// What one <c>dotnet build</c> builds: run in <paramref name="Directory"/> with <paramref name="BuildFile"/> (relative to it), or
/// with no file, which builds the directory's only solution or project.
/// </summary>
/// <param name="Directory">The full path the build runs in.</param>
/// <param name="BuildFile">The solution, solution filter or project it names; null for none.</param>
internal sealed record BuildScope(string Directory, string? BuildFile)
{
    /// <summary>The builds of the repository at <paramref name="root"/> under its own <c>lockflight.yaml</c>, if it has one (<see cref="Of"/>).</summary>
    public static IReadOnlyList<BuildScope> OfRepository(string root) =>
        Of(ManifestLocator.Discover(root).Manifest is Manifest manifest && SameDirectory(manifest.Root, root) ? manifest : null, root);

    /// <summary>
    /// The builds the gates and red/green run in a repository at <paramref name="root"/> under <paramref name="manifest"/>: each
    /// .NET unit with its <c>build:</c> file in a workspace; otherwise the root with <c>gates.build</c> (the gates) and the root as
    /// it is (red/green). Without a manifest, the root as it is.
    /// </summary>
    public static IReadOnlyList<BuildScope> Of(Manifest? manifest, string root)
    {
        if (manifest is null)
        {
            return [new BuildScope(root, BuildFile: null)];
        }

        if (manifest.Units.Count > 0)
        {
            return [.. manifest.Units
                .Where(unit => WorkspaceGateRunner.BuildsWithDotnet(manifest, unit))
                .Select(unit => new BuildScope(Path.Combine(root, unit.Path.Replace('/', Path.DirectorySeparatorChar)), unit.Build))];
        }

        return manifest.Gates.BuildFile is string buildFile
            ? [new BuildScope(root, buildFile), new BuildScope(root, BuildFile: null)]
            : [new BuildScope(root, BuildFile: null)];
    }

    private static bool SameDirectory(string left, string right) =>
        string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
