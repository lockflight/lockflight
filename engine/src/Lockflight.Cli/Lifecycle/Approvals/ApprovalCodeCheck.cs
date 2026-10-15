namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The result of checking a typed approval code; when it matched, the feature and artifact hashes the person was shown with it.
/// </summary>
internal sealed record ApprovalCodeCheck(ApprovalCodeState State, string? FeatureId, IReadOnlyList<(string Name, string Hash)> Artifacts)
{
    public static ApprovalCodeCheck Of(ApprovalCodeState state) => new(state, null, []);
}
