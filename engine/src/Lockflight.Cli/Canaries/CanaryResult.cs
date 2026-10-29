namespace Lockflight.Cli.Canaries;

/// <summary>How one planted violation came out.</summary>
internal enum CanaryResult
{
    /// <summary>The control rejected the violation.</summary>
    Caught = 0,

    /// <summary>The control let the violation through: a safeguard is not working.</summary>
    Missed = 1,

    /// <summary>
    /// The canary could not judge: its harmless twin was rejected too (so a rejection proves nothing), or what it needs (git, a
    /// generated project that builds) is missing. Counts as a failure, never as caught.
    /// </summary>
    Broken = 2,
}
