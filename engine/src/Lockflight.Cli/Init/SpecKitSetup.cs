namespace Lockflight.Cli.Init;

/// <summary>How far Spec Kit is set up in a project when <c>init</c> finishes: what the next steps still have to do.</summary>
internal enum SpecKitSetup
{
    /// <summary>No Spec Kit (<c>.specify/</c> is missing): set it up with the Lockflight preset.</summary>
    None = 0,

    /// <summary>Spec Kit without the Lockflight preset: the team's Spec Kit, adopted with <c>adopt plan</c> and <c>adopt apply</c>.</summary>
    WithoutPreset = 1,

    /// <summary>Spec Kit with the Lockflight preset installed and enabled, as the init skill sets it up before <c>init</c>.</summary>
    WithPreset = 2,
}
