namespace Lockflight.Cli.Routing;

/// <summary>How a deleted file and an added file were paired as one move.</summary>
internal enum MoveKind
{
    /// <summary>The same blob on both sides: the move adds and removes no lines.</summary>
    Exact = 1,

    /// <summary>Line multisets at least 80 % alike (Dice): only the differing lines count toward size.</summary>
    Edited = 2,
}
