namespace Lockflight.Cli.Updating;

/// <summary>How <c>~/.lockflight/bin/lockflight</c> reaches the engine installed under <c>~/.lockflight/tools/&lt;version&gt;</c>.</summary>
internal enum EngineLayout
{
    /// <summary>A symbolic link into the tool path, replaced in one rename (macOS and Linux).</summary>
    Link = 0,

    /// <summary>
    /// A copy of the native engine from the tool path's store, with its version in <c>bin/lockflight.version</c> (Windows, where
    /// creating a symbolic link needs developer mode or an elevated session, and the tool path's own launcher is not one file).
    /// </summary>
    Copy = 1,
}
