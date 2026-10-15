namespace Lockflight.Cli.Updating;

/// <summary>What updating the user install from a package feed came to.</summary>
internal enum PackageUpdateStatus
{
    /// <summary>A newer engine was installed and <c>~/.lockflight/bin/lockflight</c> now points at it.</summary>
    Installed = 0,

    /// <summary>The newest engine on the feed is the one already installed.</summary>
    UpToDate = 1,

    /// <summary>The installed engine is newer than anything on the feed (a development build), so it was kept.</summary>
    KeptNewer = 2,

    /// <summary>The feed could not be read, the package did not install, or the installed engine does not run.</summary>
    Failed = 3,
}
