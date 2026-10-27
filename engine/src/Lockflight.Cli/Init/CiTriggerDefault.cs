using Lockflight.Cli.Config;

namespace Lockflight.Cli.Init;

/// <summary>The CI trigger <c>init</c> uses when <c>--ci</c> is not given.</summary>
internal static class CiTriggerDefault
{
    /// <summary>
    /// Pull requests, unless the engine pinned is a development build: no package feed has one, so a workflow that installs it
    /// on every pull request would fail every time. Such a project gets a workflow that runs only when started by hand.
    /// </summary>
    public static CiTrigger For(string engineVersion) =>
        EngineVersion.IsDevelopmentBuild(engineVersion) ? CiTrigger.Manual : CiTrigger.PullRequest;
}
