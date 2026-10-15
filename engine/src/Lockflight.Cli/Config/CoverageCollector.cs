namespace Lockflight.Cli.Config;

/// <summary>Which Microsoft.Testing.Platform coverage extension the project's test projects reference.</summary>
internal enum CoverageCollector
{
    /// <summary>Coverage is not collected; the patch-coverage gate reports itself as not configured.</summary>
    None = 0,

    /// <summary>Microsoft.Testing.Extensions.CodeCoverage (<c>--coverage</c>); closed-source licence, shown at onboarding.</summary>
    Microsoft = 1,

    /// <summary>coverlet.MTP (<c>--coverlet</c>); MIT, the default for new setups.</summary>
    Coverlet = 2,
}
