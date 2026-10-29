namespace Lockflight.Cli.Config;

/// <summary>
/// A named set of routing thresholds (<c>routing.preset</c>). A preset seeds the values <c>init</c> writes and labels them; the
/// numbers spelled out in the manifest are what judges a change.
/// </summary>
internal enum RoutingPreset
{
    /// <summary>Larger fixes, fewer requirements (<c>relaxed</c>).</summary>
    Relaxed = 1,

    /// <summary>The thresholds a new project starts with (<c>default</c>).</summary>
    Default = 2,

    /// <summary>Every change with production lines is at least <c>behaviour</c>; more requirements (<c>strict</c>).</summary>
    Strict = 3,
}
