using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// The base branch's policy as the classifier reads it: the <c>routing:</c> settings, the globs of the policy files a change to
/// which is <c>critical.policy</c> (the engine's protected list for this manifest, CODEOWNERS and the workflows a platform ruleset
/// requires; Lockflight's feature records are not policy), and where the policy was read.
/// </summary>
internal sealed record RoutingPolicy(RoutingSettings Settings, IReadOnlyList<string> PolicyGlobs, PolicySource Source)
{
    private const string FeatureRecords = ".lockflight/features/";

    private static readonly string[] CodeOwners = ["CODEOWNERS", ".github/CODEOWNERS", "docs/CODEOWNERS", ".gitlab/CODEOWNERS", ".bitbucket/CODEOWNERS"];

    /// <summary>The policy of a base branch's manifest; null means the base has none, so the default preset and protected list apply.</summary>
    public static RoutingPolicy From(Manifest? manifest, PolicySource source)
    {
        RoutingSettings settings = manifest?.Routing ?? RoutingSettings.Default;
        IReadOnlyList<string> protectedGlobs = manifest is null ? DefaultProtectedPaths.Globs : DefaultProtectedPaths.For(manifest);
        return new RoutingPolicy(settings, [.. protectedGlobs.Concat(CodeOwners).Concat(settings.RequiredWorkflows).Distinct(StringComparer.Ordinal)], source)
        {
            Approvals = manifest?.Approvals ?? ApprovalSettings.Default,
        };
    }

    /// <summary>The base branch's <c>approvals:</c> section, for the spec-first requirement.</summary>
    public ApprovalSettings Approvals { get; init; } = ApprovalSettings.Default;

    /// <summary>The <c>routing.generatedPaths</c> and <c>routing.nonProduction</c> globs: left out of size and shape only.</summary>
    public IReadOnlyList<string> ExcludedGlobs => [.. Settings.GeneratedPaths, .. Settings.NonProduction];

    /// <summary>The policy glob <paramref name="path"/> matches; null when it is not a policy file (feature records never are).</summary>
    public string? PolicyGlobOf(string path) =>
        path.StartsWith(FeatureRecords, StringComparison.OrdinalIgnoreCase) ? null : PolicyGlobs.FirstOrDefault(glob => PathRules.Matches(glob, path));

    /// <summary>The <c>routing.critical</c> glob <paramref name="path"/> matches; null when none does.</summary>
    public string? CriticalGlobOf(string path) => Settings.Critical.FirstOrDefault(glob => PathRules.Matches(glob, path));
}
