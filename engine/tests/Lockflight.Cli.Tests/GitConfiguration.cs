using System.Runtime.CompilerServices;

namespace Lockflight.Cli.Tests;

/// <summary>
/// Keeps the developer's global git configuration (<c>~/.gitconfig</c> and <c>$XDG_CONFIG_HOME/git/config</c>) out of every git the
/// tests start, directly or through the engine: <c>commit.gpgsign</c>, <c>init.defaultBranch</c>, <c>core.quotePath</c> or a hook path
/// there would otherwise change what a scratch repository does, so a test could pass on CI and fail on a laptop or the other way
/// round. <c>GIT_CONFIG_GLOBAL</c> names a file that does not exist, which git reads as empty. The system configuration is left as
/// it is: CI's runners have one too.
/// </summary>
internal static class GitConfiguration
{
    [ModuleInitializer]
    internal static void IgnoreTheGlobalConfiguration() =>
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", Path.Combine(Path.GetTempPath(), $"lockflight-tests-no-gitconfig-{Guid.NewGuid():N}"));
}
