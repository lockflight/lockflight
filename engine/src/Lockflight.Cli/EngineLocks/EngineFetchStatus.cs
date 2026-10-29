namespace Lockflight.Cli.EngineLocks;

/// <summary>How fetching an engine (or a release's hashes) from a package feed came out.</summary>
internal enum EngineFetchStatus
{
    /// <summary>Downloaded, extracted and, for an engine, its hash equals the expected one.</summary>
    Fetched = 1,

    /// <summary>The feed has no such package or version.</summary>
    NotFound = 2,

    /// <summary>The feed refused the credentials (HTTP 401 or 403): it may need a NuGet credential provider, which only <c>dotnet</c> runs.</summary>
    Unauthorized = 3,

    /// <summary>The executable's hash is not the expected one; nothing was kept.</summary>
    HashMismatch = 4,

    /// <summary>The package or the feed's answer is not what a release publishes (no single executable, an unsafe entry, no hashes).</summary>
    Invalid = 5,

    /// <summary>The feed could not be reached or answered with another error.</summary>
    Unreachable = 6,
}
