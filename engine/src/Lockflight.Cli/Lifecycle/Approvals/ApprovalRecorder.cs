using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// Records a person's approval, whichever way they gave it (<c>lockflight approve</c> in a terminal or <c>/lockflight:approve</c> in
/// a session), so both write the same record and ledger line.
/// </summary>
internal sealed class ApprovalRecorder(CliContext context, string root)
{
    private const string UnknownApprover = "unknown";
    private const int HashPrefixLength = 12;

    /// <param name="artifacts">The artifact hashes the person approved (the record binds exactly these).</param>
    public async Task<ApprovalRecording> RecordAsync(
        Feature feature,
        ApprovalPhase phase,
        IReadOnlyList<(string Name, string Hash)> artifacts,
        ApprovalChannel channel,
        CancellationToken cancellationToken)
    {
        string approvedBy = await Git.ConfigValueAsync(root, "user.email", cancellationToken) ?? UnknownApprover;
        string record = new ApprovalStore(feature).Save(phase, artifacts, approvedBy, context.Time.GetUtcNow(), ToolPin.RunningVersion);

        // The approver stays in the approval record; the ledger is readable by the agent and needs only what was approved.
        string phaseName = ApprovalPhaseNames.ToName(phase);
        string summary = string.Join(',', artifacts.Select(artifact => $"{artifact.Name}={artifact.Hash[..Math.Min(HashPrefixLength, artifact.Hash.Length)]}"));
        LedgerEvent approval = new(LedgerEventType.ApprovalRecorded, LedgerSignatures.ApprovePrefix + phaseName, $"{feature.Id} {phaseName} approved", null)
        {
            Approval = new LedgerApproval(feature.Id, phaseName, summary, channel),
        };
        await new LedgerWriter(root, context.Time).AppendAsync(approval, cancellationToken);
        return new ApprovalRecording(record, approvedBy, summary);
    }
}
