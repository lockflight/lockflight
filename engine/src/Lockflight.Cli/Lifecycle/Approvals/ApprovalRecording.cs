using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The outcome of recording an approval: the record written (and its signature, when signed), or why nothing was recorded with
/// the exit code that fits.
/// </summary>
internal sealed record ApprovalRecording(string? RecordPath, string? ApprovedBy, bool Signed, string ArtifactSummary, ExitCode FailureCode, string? Problem)
{
    public bool Recorded => RecordPath is not null;

    public static ApprovalRecording Done(string recordPath, string approvedBy, bool signed, string artifactSummary) =>
        new(recordPath, approvedBy, signed, artifactSummary, ExitCode.Pass, null);

    public static ApprovalRecording Failed(ExitCode exitCode, string problem) => new(null, null, false, string.Empty, exitCode, problem);
}
