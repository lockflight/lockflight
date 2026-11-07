using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="StructuralOutcome"/>.</summary>
internal static class StructuralOutcomeNames
{
    public static string ToName(StructuralOutcome outcome) => outcome switch
    {
        StructuralOutcome.Preserved => "preserved",
        StructuralOutcome.ArchitectureRule => "architecture-rule",
        StructuralOutcome.BehaviourChanged => "behaviour-changed",
        StructuralOutcome.FailsAtHead => "fails-at-head",
        StructuralOutcome.Uncheckable => "uncheckable",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown structural outcome."),
    };
}
