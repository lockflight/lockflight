namespace Lockflight.Cli.Proposals;

/// <summary>
/// A checked request: the project file (relative to the project root, '/'-separated), its current and proposed bytes, and what
/// the person needs to know before applying.
/// </summary>
/// <param name="OldContent">The file's bytes now; null when it does not exist yet.</param>
/// <param name="NewContent">The proposed bytes; null for a deletion.</param>
/// <param name="Mode">The file's Unix permissions, kept on both sides so the patch changes content only; null on Windows or for a new file.</param>
/// <param name="ProtectedRule">The protected-path rule the file falls under; null when it is not protected.</param>
/// <param name="HighRisk">Why the change is high risk; null when it is not.</param>
internal sealed record ProposalTarget(
    string RelativePath,
    byte[]? OldContent,
    byte[]? NewContent,
    UnixFileMode? Mode,
    string? ProtectedRule,
    string? HighRisk)
{
    public ProposalChange Change => OldContent is null ? ProposalChange.Added : NewContent is null ? ProposalChange.Deleted : ProposalChange.Modified;
}
