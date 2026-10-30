namespace Lockflight.Cli.Adoption;

/// <summary>What <c>init</c> does with a plan item (keep and flag by default).</summary>
internal enum AdoptionAction
{
    /// <summary>Left as it is; the team's own control, now protected from the agent.</summary>
    Keep = 1,

    /// <summary>Its paths are added to <c>protected:</c> in <c>lockflight.yaml</c>; the hook itself stays.</summary>
    Protect = 2,

    /// <summary>Kept, with a warning about how it interacts with Lockflight.</summary>
    Flag = 3,

    /// <summary>Lockflight adds something (a unit, a CLAUDE.md block).</summary>
    Add = 4,

    /// <summary>Upgraded to the pinned version with Lockflight's preset; customised files are reported, never overwritten.</summary>
    Upgrade = 5,

    /// <summary>Marked as predating Lockflight: trace and status say so, nothing blocks it.</summary>
    Mark = 6,
}
