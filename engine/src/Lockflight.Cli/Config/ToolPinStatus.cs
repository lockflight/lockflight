namespace Lockflight.Cli.Config;

/// <summary>What reading the project's .NET local tool manifest found about the engine pin.</summary>
internal enum ToolPinStatus
{
    /// <summary>No tool manifest pins <c>lockflight.cli</c>.</summary>
    None = 0,

    /// <summary>A tool manifest pins a version.</summary>
    Pinned = 1,

    /// <summary>A tool manifest exists but cannot be read (malformed JSON, wrong shape, I/O error).</summary>
    Unreadable = 2,
}
