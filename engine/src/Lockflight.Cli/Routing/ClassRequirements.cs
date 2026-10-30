using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>
/// What a change class requires under the base branch's policy. Every per-class key is "from class k" (<c>routing.require.*From</c>,
/// <c>approvals.specFirstFrom</c>) and the coverage thresholds never decrease, so whatever a class requires, every class above it
/// requires too. Every kind is listed, <see cref="RequirementLevel.Off"/> included, in a fixed order.
/// <list type="bullet">
/// <item>The gates, suppressions and docs references are required at every class.</item>
/// <item>Patch coverage applies at its class threshold when a collector is set; from <c>feature</c> it is required (NOT-EARNED
/// without a collector).</item>
/// <item>EF Core pending model changes apply when an EF project changed and <c>dotnet-ef</c> is pinned; required from
/// <c>feature</c>.</item>
/// <item>Red→green, and from <c>routing.require.specFrom</c> spec and trace, apply only when production code lines changed: a
/// docs-only or test-only pull request needs neither.</item>
/// <item>The review is advisory from <c>reviewFrom</c> (run when a key is set) and required with a key from <c>reviewKeyFrom</c>.</item>
/// <item>Declared architecture tests apply from <c>architectureFrom</c>; from <c>architectureDeclaredFrom</c> none declared is NOT-EARNED.</item>
/// <item>Spec-first is required from <c>approvals.specFirstFrom</c>, and applies at every class when <c>approvals.default</c> is
/// spec-first. Mutation testing is not part of this version.</item>
/// </list>
/// </summary>
internal static class ClassRequirements
{
    public static RequirementSet For(ChangeClass changeClass, RoutingPolicy policy)
    {
        RoutingSettings settings = policy.Settings;
        RoutingRequirements require = settings.Require;
        bool featureOrAbove = changeClass >= ChangeClass.Feature;
        int coverage = changeClass switch
        {
            ChangeClass.Fix => settings.Coverage.Fix,
            ChangeClass.Behaviour => settings.Coverage.Behaviour,
            ChangeClass.Feature => settings.Coverage.Feature,
            _ => settings.Coverage.Critical,
        };
        return new RequirementSet(changeClass,
        [
            new(RequirementKind.Gates, RequirementLevel.Required, Threshold: null),
            new(RequirementKind.PatchCoverage, featureOrAbove ? RequirementLevel.Required : RequirementLevel.WhenApplicable, coverage),
            new(RequirementKind.Suppressions, RequirementLevel.Required, Threshold: null),
            new(RequirementKind.DocsReferences, RequirementLevel.Required, Threshold: null),
            new(RequirementKind.EfPendingModelChanges, featureOrAbove ? RequirementLevel.Required : RequirementLevel.WhenApplicable, Threshold: null),
            new(RequirementKind.RedGreen, RequirementLevel.WhenApplicable, Threshold: null),
            new(RequirementKind.SpecAndTrace, From(changeClass, require.SpecFrom) ? RequirementLevel.Required : RequirementLevel.WhenApplicable, Threshold: null),
            new(RequirementKind.Review, Review(changeClass, require), Threshold: null),
            new(RequirementKind.ArchitectureTests, Architecture(changeClass, require), Threshold: null),
            new(RequirementKind.SpecFirst, SpecFirst(changeClass, policy.Approvals), Threshold: null),
            new(RequirementKind.Mutation, RequirementLevel.Off, Threshold: null),
        ]);
    }

    private static RequirementLevel Review(ChangeClass changeClass, RoutingRequirements require)
    {
        if (From(changeClass, require.ReviewKeyFrom) && From(changeClass, require.ReviewFrom))
        {
            return RequirementLevel.RequiredWithKey;
        }

        return From(changeClass, require.ReviewFrom) ? RequirementLevel.Advisory : RequirementLevel.Off;
    }

    private static RequirementLevel Architecture(ChangeClass changeClass, RoutingRequirements require)
    {
        if (From(changeClass, require.ArchitectureDeclaredFrom))
        {
            return RequirementLevel.Required;
        }

        return From(changeClass, require.ArchitectureFrom) ? RequirementLevel.WhenApplicable : RequirementLevel.Off;
    }

    private static RequirementLevel SpecFirst(ChangeClass changeClass, ApprovalSettings approvals)
    {
        if (From(changeClass, approvals.SpecFirstFrom))
        {
            return RequirementLevel.Required;
        }

        return approvals.Mode == ApprovalMode.SpecFirst ? RequirementLevel.WhenApplicable : RequirementLevel.Off;
    }

    /// <summary>Whether a requirement that switches on at <paramref name="from"/> (null: never) is on at <paramref name="changeClass"/>.</summary>
    private static bool From(ChangeClass changeClass, ChangeClass? from) => from is ChangeClass start && changeClass >= start;
}
