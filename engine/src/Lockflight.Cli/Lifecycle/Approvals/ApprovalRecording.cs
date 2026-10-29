namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>The outcome of recording an approval: the record written, who it names and what it bound.</summary>
internal sealed record ApprovalRecording(string RecordPath, string ApprovedBy, string ArtifactSummary);
