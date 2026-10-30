namespace Lockflight.Cli.EngineLocks;

/// <summary>Where a lock's engine comes from (<c>channel</c>).</summary>
internal enum EngineLockChannel
{
    /// <summary>A published release; every released platform has a hash. The only channel a committed lock may hold (<c>release</c>).</summary>
    Release = 1,

    /// <summary>A build from source, with the local platform's hash only; allowed only in the git-ignored local lock (<c>development</c>).</summary>
    Development = 2,
}
