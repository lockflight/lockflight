using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// One path a pull request changes: its path relative to the project root ('/'-separated, a child repository's unit path in
/// front), what happened to it, the blob ids and modes on each side (null on the side where it does not exist; a working-tree
/// side gets the id git would store), whether git treats its content as binary, and the lines it adds (numbered at head) and
/// removes (numbered at base), as the zero-context diff gives them.
/// </summary>
internal sealed record ChangedFile(
    string Path,
    ChangeKind Kind,
    string? OldBlob,
    string? NewBlob,
    string? OldMode,
    string? NewMode,
    bool Binary,
    IReadOnlyList<DiffLine> Added,
    IReadOnlyList<DiffLine> Removed)
{
    /// <summary>The git mode of a submodule entry (a gitlink).</summary>
    public const string SubmoduleMode = "160000";

    /// <summary>The git mode of a symbolic link.</summary>
    public const string SymbolicLinkMode = "120000";

    public bool ExistsAtBase => Kind != ChangeKind.Added;

    public bool ExistsAtHead => Kind != ChangeKind.Deleted;
}
