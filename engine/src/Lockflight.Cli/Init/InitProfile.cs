namespace Lockflight.Cli.Init;

/// <summary>How much of Lockflight an <c>init</c> sets up.</summary>
internal enum InitProfile
{
    /// <summary>Engine, manifest, guard, Stop hook (off), one CI job; no build changes.</summary>
    Lite = 1,

    /// <summary>Lite plus what the lifecycle needs in the repository (evidence files kept byte-exact for signatures); Spec Kit and its preset are installed by the init skill.</summary>
    Standard = 2,
}
