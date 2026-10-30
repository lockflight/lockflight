namespace Lockflight.Cli.EngineLocks;

/// <summary>How an engine lock compares with the release its version names.</summary>
internal enum LockReleaseVerdict
{
    /// <summary>Every platform's hash is the one the release published.</summary>
    Matches = 1,

    /// <summary>A hash differs from the release's: the lock pins an engine no release shipped.</summary>
    Differs = 2,

    /// <summary>The feed has no published hashes for the version (no such release, or one before engine locks).</summary>
    NoRelease = 3,

    /// <summary>The lock cannot be read.</summary>
    Unreadable = 4,

    /// <summary>The hashes match, but the attestation that was asked for did not verify.</summary>
    Unattested = 5,
}
