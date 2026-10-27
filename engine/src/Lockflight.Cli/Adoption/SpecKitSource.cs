namespace Lockflight.Cli.Adoption;

/// <summary>Where <c>uvx</c> takes a Spec Kit version from: the <c>--from</c> requirement and a short name for messages.</summary>
/// <param name="From">The requirement passed to <c>uvx --from</c>, such as <c>specify-cli==1.1.3</c>.</param>
/// <param name="Name">Where it comes from, such as <c>PyPI</c> or <c>GitHub tag v1.1.0</c>.</param>
internal sealed record SpecKitSource(string From, string Name)
{
    /// <summary>True for the package index, the usual source; false for a git tag.</summary>
    public bool IsPackageIndex => From.StartsWith(SpecKitSources.PackageName, StringComparison.Ordinal);
}
