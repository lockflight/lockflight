using Lockflight.Cli.Config;

namespace Lockflight.Cli.Init;

/// <summary>What <c>lockflight init</c> was asked to do, parsed from its command line.</summary>
/// <param name="DryRun"><c>--dry-run</c>: print what init would write and the adoption plan, and write nothing.</param>
/// <param name="Shell"><c>--unit-of &lt;url&gt;</c> (and <c>--unit-path</c>): this repository is a unit of that workspace shell.</param>
/// <param name="SpecKitPreset"><c>--speckit-preset &lt;dir&gt;</c>: upgrade an existing Spec Kit with the Lockflight preset from there.</param>
internal sealed record InitOptions(
    bool Link,
    bool Sandbox,
    InitProfile Profile,
    CiTrigger? RequestedCi,
    CiPlatform Platform,
    InitQuality Quality,
    bool DryRun,
    ShellSettings? Shell,
    string? SpecKitPreset)
{
    /// <summary><c>--preset relaxed|default|strict</c>: the routing preset a new manifest spells out (default <c>default</c>).</summary>
    public RoutingPreset Preset { get; init; } = RoutingPreset.Default;
}
