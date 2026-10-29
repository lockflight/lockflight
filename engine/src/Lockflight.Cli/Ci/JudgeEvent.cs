namespace Lockflight.Cli.Ci;

/// <summary>What started the CI run; each event has its own requirements.</summary>
internal enum JudgeEvent
{
    /// <summary>A pull request: every check its change class requires.</summary>
    PullRequest = 1,

    /// <summary>A push to the default branch: green and the gates at head only.</summary>
    Push = 2,

    /// <summary>A merge queue group: green and the gates at the group's head only.</summary>
    MergeGroup = 3,
}
