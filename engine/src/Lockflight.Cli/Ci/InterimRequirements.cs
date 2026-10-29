using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The provisional class → requirements table the judge applies until risk routing owns it. A low-risk change needs no red/green
/// cycle, trace or approval; a dependency-only change needs no red cycle; review counts only for a critical change.
/// </summary>
internal static class InterimRequirements
{
    public static RequirementSet For(ChangeClass changeClass, InterimShape shape)
    {
        RequirementLevel redGreen = shape is InterimShape.LowRisk or InterimShape.DependencyOnly ? RequirementLevel.Off : RequirementLevel.Required;
        RequirementLevel trace = shape == InterimShape.LowRisk ? RequirementLevel.Off
            : changeClass >= ChangeClass.Feature ? RequirementLevel.Required
            : RequirementLevel.WhenApplicable;
        RequirementLevel review = changeClass == ChangeClass.Critical ? RequirementLevel.RequiredWithKey : RequirementLevel.Advisory;
        RequirementLevel specFirst = shape == InterimShape.LowRisk ? RequirementLevel.Off
            : changeClass >= ChangeClass.Feature ? RequirementLevel.Required
            : RequirementLevel.Advisory;
        return new RequirementSet(changeClass,
        [
            new Requirement(RequirementKind.Gates, RequirementLevel.Required, Threshold: null),
            new Requirement(RequirementKind.RedGreen, redGreen, Threshold: null),
            new Requirement(RequirementKind.SpecAndTrace, trace, Threshold: null),
            new Requirement(RequirementKind.Review, review, Threshold: null),
            new Requirement(RequirementKind.SpecFirst, specFirst, Threshold: null),
        ]);
    }
}
