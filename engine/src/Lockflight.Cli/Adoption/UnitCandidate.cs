namespace Lockflight.Cli.Adoption;

/// <summary>A directory that looks like a unit: its path relative to the root, whether it is its own repository, whether it holds a solution.</summary>
internal sealed record UnitCandidate(string Path, bool OwnRepository, bool DotnetSolution)
{
    /// <summary>The <c>units:</c> entry lines for it: <c>kind: none</c> when it has no solution (a TypeScript or gitops repository).</summary>
    public IReadOnlyList<string> ManifestEntry => DotnetSolution ? [$"path: {Path}"] : [$"path: {Path}", "kind: none"];
}
