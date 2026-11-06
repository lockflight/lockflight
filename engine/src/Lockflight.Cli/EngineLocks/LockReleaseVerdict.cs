namespace Lockflight.Cli.EngineLocks;

/// <summary>How an engine lock compares with the release its version names, and with the vendored install script.</summary>
internal enum LockReleaseVerdict
{
    /// <summary>Every platform's hash is the one the release published.</summary>
    Matches = 1,

    /// <summary>A platform's hash differs from the release's: the lock pins an engine no release shipped.</summary>
    Differs = 2,

    /// <summary>The feed has no published hashes for the version (no such release, one before engine locks, or an unreadable package).</summary>
    NoRelease = 3,

    /// <summary>The lock cannot be read: not JSON, a key repeated or unknown, a malformed hash, or a released platform without one.</summary>
    Unreadable = 4,

    /// <summary>The hashes match, but the attestation that was asked for did not verify.</summary>
    Unattested = 5,

    /// <summary>The install script hash the lock pins is not the one the release published.</summary>
    ScriptDiffers = 6,

    /// <summary>The release published no install script hash, so it cannot vouch for the script the lock pins.</summary>
    ScriptUnpublished = 7,

    /// <summary>The vendored install script is not the one the lock pins.</summary>
    VendoredScriptDiffers = 8,

    /// <summary>There is no vendored install script to compare with the lock.</summary>
    VendoredScriptMissing = 9,
}
