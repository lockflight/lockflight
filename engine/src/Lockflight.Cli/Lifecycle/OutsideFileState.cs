namespace Lockflight.Cli.Lifecycle;

/// <summary>What a file named by an <c>[outside .NET: …]</c> marker says about its requirement.</summary>
internal enum OutsideFileState
{
    /// <summary>The file exists and names the requirement ID.</summary>
    Names = 0,

    /// <summary>The file does not exist (at head, in CI), or is a directory or a symbolic link.</summary>
    Missing = 1,

    /// <summary>The file exists but does not name the requirement ID.</summary>
    DoesNotName = 2,

    /// <summary>The path is absolute or leaves the project root, so it is never read.</summary>
    OutsideProject = 3,

    /// <summary>The path is under <c>specs/</c>: the spec, its tasks or plan always name the requirement, so they verify nothing.</summary>
    InSpecs = 4,
}
