namespace Lockflight.Cli.Proposals;

/// <summary>What a proposed patch does to its file.</summary>
internal enum ProposalChange
{
    /// <summary>The file exists and gets new content.</summary>
    Modified = 1,

    /// <summary>The file does not exist yet and is created.</summary>
    Added = 2,

    /// <summary>The file exists and is removed (<c>--delete</c>).</summary>
    Deleted = 3,
}
