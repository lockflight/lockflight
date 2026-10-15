using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class ClaudeMdBlockTests
{
    [Fact]
    public void AnOlderBlockIsReplacedInPlaceAndTheTeamsTextKept()
    {
        string old = $"# Team\n\nOur rules.\n\n{ClaudeMdBlock.Begin}\n## Lockflight\nold text\n{ClaudeMdBlock.End}\n\n## After\nmore\n";

        string updated = ClaudeMdBlock.Upsert(old);

        Assert.StartsWith("# Team\n\nOur rules.\n\n", updated, StringComparison.Ordinal);
        Assert.EndsWith("\n\n## After\nmore\n", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("old text", updated, StringComparison.Ordinal);
        Assert.True(ClaudeMdBlock.IsCurrent(updated));
        Assert.Equal(updated, ClaudeMdBlock.Upsert(updated));
    }

    [Fact]
    public void AMissingBlockIsAppended()
    {
        string updated = ClaudeMdBlock.Upsert("# Team\n");

        Assert.StartsWith("# Team\n\n", updated, StringComparison.Ordinal);
        Assert.True(ClaudeMdBlock.IsCurrent(updated));
    }

    [Fact]
    public void TheBlockExplainsTheTermsItUses()
    {
        Assert.Contains("gate run", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("accept build warnings, failing tests or package advisories", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("discard a feature's test-first evidence", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("git apply", ClaudeMdBlock.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockSendsProtectedChangesThroughProposeAndNeverAsksToBypassTheGuard()
    {
        Assert.Contains("lockflight propose <path> --from <file>", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("never ask", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/<task>.patch", ClaudeMdBlock.Text, StringComparison.Ordinal);
    }
}
