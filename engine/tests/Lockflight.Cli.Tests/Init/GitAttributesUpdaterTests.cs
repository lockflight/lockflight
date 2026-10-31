using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class GitAttributesUpdaterTests
{
    [Fact]
    public void TheRuleIsAppendedOnceAndExistingLinesStay()
    {
        (string first, bool changed) = GitAttributesUpdater.Update("* text=auto");
        (string second, bool changedAgain) = GitAttributesUpdater.Update(first);

        Assert.True(changed);
        Assert.StartsWith("* text=auto\n", first, StringComparison.Ordinal);
        Assert.Contains(GitAttributesUpdater.Rule, first, StringComparison.Ordinal);
        Assert.False(changedAgain);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TheInstallScriptKeepsLfEndingsOnEveryCheckout()
    {
        (string first, bool changed) = GitAttributesUpdater.UpdateInstallScript(GitAttributesUpdater.Rule + "\n");
        (_, bool changedAgain) = GitAttributesUpdater.UpdateInstallScript(first);

        Assert.True(changed);
        Assert.Contains(".lockflight/ci/install.sh text eol=lf", first.Split('\n'));
        Assert.False(changedAgain);
    }
}
