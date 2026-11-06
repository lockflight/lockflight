using System.Runtime.CompilerServices;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Tests.Goldens;

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
    public void TheBlockLockflight031WroteIsRecognisedAndReplacedWithTheCurrentOne()
    {
        string released = Case("lockflight-0.3.1.txt");

        string updated = ClaudeMdBlock.Upsert(released);

        Assert.True(ClaudeMdBlock.IsPresent(released));
        Assert.False(ClaudeMdBlock.IsCurrent(released));
        Assert.Equal("# Team rules\n\nKeep it small.\n\n" + ClaudeMdBlock.Text + "\n\n## After\n\nMore of ours.\n", updated);
    }

    [Fact]
    public void TheBlockSendsScratchFilesAndExperimentsOutsideTheProject()
    {
        Assert.Contains("a scratch directory outside the project", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("`mktemp -d`", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("your scratch directory", ClaudeMdBlock.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockMatchesItsGoldenFile() => GoldenFiles.Verify("claude-md-block", ClaudeMdBlock.Text);

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
        Assert.Contains("lockflight review dismiss", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("git apply", ClaudeMdBlock.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlockSendsProtectedChangesThroughProposeAndNeverAsksToBypassTheGuard()
    {
        Assert.Contains("lockflight propose <path> --from <file>", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.Contains("never ask", ClaudeMdBlock.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/<task>.patch", ClaudeMdBlock.Text, StringComparison.Ordinal);
    }

    private static string Case(string name, [CallerFilePath] string path = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "ClaudeMdBlockCases", name)).ReplaceLineEndings("\n");
}
