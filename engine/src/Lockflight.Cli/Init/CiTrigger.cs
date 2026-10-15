namespace Lockflight.Cli.Init;

/// <summary>When the CI workflow <c>init</c> writes runs the <c>pr</c> gates.</summary>
internal enum CiTrigger
{
    /// <summary>On every pull request and every push to <c>main</c> (the default).</summary>
    PullRequest = 1,

    /// <summary>Only when someone starts it by hand (<c>workflow_dispatch</c>), for projects that save CI minutes.</summary>
    Manual = 2,

    /// <summary>No workflow is written.</summary>
    None = 3,
}
