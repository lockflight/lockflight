namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The part of the CI judge's run in which a process left running was found.</summary>
internal enum SweepRegion
{
    /// <summary>Before the judge builds anything: reading the policy, the plan and the secrets scan.</summary>
    Setup = 0,

    /// <summary>Building and running the head's tests.</summary>
    Head = 1,

    /// <summary>Building and listing the base.</summary>
    Base = 2,

    /// <summary>Re-running the red commits.</summary>
    Red = 3,

    /// <summary>The checks that build again after the reds: tests since red, existing tests changed, structural checks.</summary>
    Rechecks = 4,

    /// <summary>The gates at head.</summary>
    Gates = 5,

    /// <summary>After the judge's steps, right before the evidence is written.</summary>
    Final = 6,
}
