using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The blocked report for work refused because a phase is not approved: <c>approval-missing</c> when a phase has no valid record,
/// else <c>approval-stale</c>; one need per phase, for the person to review the artifact and approve it.
/// </summary>
internal static class ApprovalBlockedReport
{
    public static BlockedReport For(string source, string root, Feature feature, IReadOnlyList<ApprovalCheck> notValid, DateTimeOffset at, string? head)
    {
        ApprovalStore store = new(feature);
        bool stale = notValid.All(check => check.State == ApprovalState.Stale);
        BlockedNeed[] needs = [.. notValid.Select(check =>
        {
            string phase = ApprovalPhaseNames.ToName(check.Phase);
            string[] paths = [.. ApprovalPhaseNames.ArtifactsOf(check.Phase).Select(name => Relative(root, Path.Combine(feature.SpecDirectory, name)))];
            return BlockedNeed.Of(BlockedWho.Person, BlockedAction.Approve, $"Review {string.Join(" and ", paths)} and approve the {phase}", humanOnly: true) with { Paths = paths };
        })];
        string summary = string.Join("; ", notValid.Select(check => check.State == ApprovalState.Stale
            ? $"the {ApprovalPhaseNames.ToName(check.Phase)} changed after its approval"
            : $"the {ApprovalPhaseNames.ToName(check.Phase)} has no valid approval"));
        return BlockedReport.Of(at, source, stale ? BlockedReasonCode.ApprovalStale : BlockedReasonCode.ApprovalMissing, summary, needs) with
        {
            Feature = feature.Id,
            Head = head,
            Evidence = [.. notValid.Select(check => store.RecordPath(check.Phase)).Where(File.Exists).Select(path => Relative(root, path))],
            Resumable = true,
        };
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
