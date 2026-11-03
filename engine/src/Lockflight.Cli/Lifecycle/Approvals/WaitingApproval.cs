namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>Which approval of a feature waits for a person.</summary>
internal static class WaitingApproval
{
    /// <summary>
    /// The spec while <c>spec.md</c> is written and its approval is missing, stale or not valid; else the tasks while
    /// <c>tasks.md</c> is written, the spec is approved and the tasks are not. Null when neither waits.
    /// </summary>
    public static ApprovalPhase? Of(Feature feature)
    {
        ApprovalStore store = new(feature);
        bool specApproved = store.Check(ApprovalPhase.Spec).State == ApprovalState.Valid;
        if (File.Exists(feature.SpecPath) && !specApproved)
        {
            return ApprovalPhase.Spec;
        }

        return specApproved && File.Exists(Path.Combine(feature.SpecDirectory, ArtifactSnapshot.TasksName)) && store.Check(ApprovalPhase.Tasks).State != ApprovalState.Valid
            ? ApprovalPhase.Tasks
            : null;
    }
}
