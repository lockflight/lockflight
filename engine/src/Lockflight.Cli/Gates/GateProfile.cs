namespace Lockflight.Cli.Gates;

/// <summary>Which set of gates runs, chosen by where the run happens.</summary>
internal enum GateProfile
{
    /// <summary>Fast checks after each edit.</summary>
    Edit = 1,

    /// <summary>Definition of done at the end of an agent turn (Stop hook).</summary>
    Stop = 2,

    /// <summary>Full set for a pull request, run in CI.</summary>
    PullRequest = 3,
}
