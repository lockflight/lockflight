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
}
