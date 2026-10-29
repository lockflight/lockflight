namespace Lockflight.Cli.Routing;

/// <summary>How reading a change set ended; only <see cref="Read"/> carries one, and the others are never "no changes".</summary>
internal enum ChangeSetReadStatus
{
    /// <summary>The change set was read.</summary>
    Read = 1,

    /// <summary>The base ref names no commit (unknown, or the all-zero id of a push without one): NOT-EARNED in CI.</summary>
    NoBase = 2,

    /// <summary>A git call failed (no merge base in a shallow clone, an unknown head, not a repository): ERROR.</summary>
    GitFailed = 3,
}
