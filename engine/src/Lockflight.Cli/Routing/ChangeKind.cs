namespace Lockflight.Cli.Routing;

/// <summary>What a pull request did to one path, as <c>git diff --raw --no-renames</c> reports it (a rename is a delete plus an add).</summary>
internal enum ChangeKind
{
    /// <summary>The path exists at head only.</summary>
    Added = 1,

    /// <summary>The path exists at base only.</summary>
    Deleted = 2,

    /// <summary>The path exists on both sides with other content or mode.</summary>
    Modified = 3,

    /// <summary>The path changed type on both sides (a file became a symbolic link or a submodule, or back).</summary>
    TypeChanged = 4,
}
