namespace Lockflight.Cli.Ledger;

/// <summary>Whether a gate run's verdict came from the Stop gate's cache of the last PASS.</summary>
internal enum StopCacheResult
{
    /// <summary>The cache was not consulted: a command-line run, or no inputs hash could be computed.</summary>
    None = 1,

    /// <summary>Nothing changed since the last PASS; the gate did not run.</summary>
    Hit = 2,

    /// <summary>The cache was consulted and did not match; the gate ran.</summary>
    Miss = 3,
}
