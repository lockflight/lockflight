using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// The rule for structural requirements, shared by local trace and the CI judge (which compare against different commits, and CI
/// also requires the test's compiled code there to be the head's): a structural requirement is one a change must keep (a
/// behaviour-preserving refactoring) or an architecture rule it establishes. A test that names only structural requirements is a
/// structural test: it is not part of a red/green cycle, and it covers its requirements when it passes at head and passes on the
/// code before the change (the start commit locally, the fork point in CI) with its own file laid over that code. One that fails
/// there on an assertion without calling production code (an architecture test reading types or sources) and passes at head
/// covers them too: a rule the change establishes. Any other failure there means the change altered behaviour, which is a normal
/// requirement's job, and such a test keeps its requirements uncovered even when another structural test counts for them. Only
/// tests that are new since the code before the change count (a test from before it, another feature's among them, proves
/// nothing about this change). Passing on both sides shows only that the test's own assertions held; what they check is for review.
/// </summary>
internal static class StructuralCheck
{
    /// <summary>The IDs of the spec's requirements marked <c>[structural]</c>.</summary>
    public static IReadOnlySet<string> Ids(IReadOnlyList<Requirement> requirements) =>
        requirements.Where(requirement => requirement.Structural).Select(requirement => requirement.Id).ToHashSet(StringComparer.Ordinal);

    /// <summary>The structural requirement IDs of the feature's spec.md; none when it has no spec.</summary>
    public static IReadOnlySet<string> IdsOf(Feature feature) =>
        File.Exists(feature.SpecPath) ? Ids(SpecDocument.Parse(File.ReadAllText(feature.SpecPath)).Requirements) : new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Whether a test's <c>Requirement</c> traits name at least one requirement, and only structural ones.</summary>
    public static bool IsStructural(IReadOnlyList<string> testRequirements, IReadOnlySet<string> structuralIds) =>
        structuralIds.Count > 0 && testRequirements.Count > 0 && testRequirements.All(structuralIds.Contains);

    /// <param name="onBase">How the test was classified on the code before the change, as a red's tests are; null when it could not run there.</param>
    /// <param name="everyRowPassedOnBase">
    /// Whether every row passed there: a red's classifier calls a theory GREEN_BEFORE when any row passes, but a structural test is
    /// preserved only when all of them do.
    /// </param>
    public static StructuralOutcome Classify(bool passesAtHead, RedClass? onBase, bool everyRowPassedOnBase) =>
        !passesAtHead ? StructuralOutcome.FailsAtHead
        : onBase switch
        {
            null => StructuralOutcome.Uncheckable,
            RedClass.GreenBefore when everyRowPassedOnBase => StructuralOutcome.Preserved,
            RedClass.RedNoProduction => StructuralOutcome.ArchitectureRule,
            _ => StructuralOutcome.BehaviourChanged,
        };

    /// <summary>The trace label of a test that counts, or the reason one does not.</summary>
    public static string Describe(StructuralTest test) => test.Outcome switch
    {
        StructuralOutcome.Preserved => $"{TraceMatrix.ShortName(test.MethodFilter)} (structural)",
        StructuralOutcome.ArchitectureRule => $"{TraceMatrix.ShortName(test.MethodFilter)} (structural: architecture rule the change establishes)",
        StructuralOutcome.BehaviourChanged => $"{TraceMatrix.ShortName(test.MethodFilter)}: behaviour changed — not structural ({test.Why})",
        StructuralOutcome.FailsAtHead => $"{TraceMatrix.ShortName(test.MethodFilter)}: structural test does not pass at head ({test.Why})",
        _ => $"{TraceMatrix.ShortName(test.MethodFilter)}: structural test could not be checked on the code before the change ({test.Why})",
    };
}
