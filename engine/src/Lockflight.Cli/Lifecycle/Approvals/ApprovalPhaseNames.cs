namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>Command-line and file names of <see cref="ApprovalPhase"/>.</summary>
internal static class ApprovalPhaseNames
{
    public static string ToName(ApprovalPhase phase) => phase switch
    {
        ApprovalPhase.Spec => "spec",
        ApprovalPhase.Tasks => "tasks",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    /// <summary>Exactly a phase name, ignoring case; anything else (empty, padded, unknown) is not a phase.</summary>
    public static bool TryParse(string name, out ApprovalPhase phase)
    {
        phase = Enum.GetValues<ApprovalPhase>().FirstOrDefault(candidate => string.Equals(ToName(candidate), name, StringComparison.OrdinalIgnoreCase));
        return phase != 0;
    }

    /// <summary>The artifacts an approval binds, relative to the feature's spec directory.</summary>
    public static IReadOnlyList<string> ArtifactsOf(ApprovalPhase phase) => phase switch
    {
        ApprovalPhase.Spec => ["spec.md"],
        ApprovalPhase.Tasks => ["tasks.md", "plan.md"],
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };
}
