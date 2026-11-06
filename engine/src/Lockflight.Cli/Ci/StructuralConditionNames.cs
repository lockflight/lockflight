namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="StructuralCondition"/>.</summary>
internal static class StructuralConditionNames
{
    public static string ToName(StructuralCondition condition) => condition switch
    {
        StructuralCondition.Routed => "routed",
        StructuralCondition.NoRedCommit => "no-red-commit",
        StructuralCondition.SpecReviewed => "spec-reviewed",
        StructuralCondition.NoBehaviourSignal => "no-behaviour-signal",
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "Unknown structural condition."),
    };
}
