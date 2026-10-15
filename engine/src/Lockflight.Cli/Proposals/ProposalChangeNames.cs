namespace Lockflight.Cli.Proposals;

/// <summary>How <see cref="ProposalChange"/> is spelled in the command's output and the ledger.</summary>
internal static class ProposalChangeNames
{
    public static string ToName(ProposalChange change) => change switch
    {
        ProposalChange.Modified => "modified",
        ProposalChange.Added => "new",
        ProposalChange.Deleted => "deleted",
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown proposal change."),
    };
}
