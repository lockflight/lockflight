using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class AmendDiffTests : IDisposable
{
    private const string ShippingFile = "tests/Shop.Tests/ShippingTests.cs";

    private const string Diff = """
        diff --git a/tests/Shop.Tests/ShippingTests.cs b/tests/Shop.Tests/ShippingTests.cs
        index 1111111..2222222 100644
        --- a/tests/Shop.Tests/ShippingTests.cs
        +++ b/tests/Shop.Tests/ShippingTests.cs
        @@ -5,1 +5,1 @@ public sealed class ShippingTests
        -    public void FreeShippingStartsAtFifty() => Assert.Equal(0m, Ship(3m, 49m));
        +    public void FreeShippingStartsAtFifty() => Assert.Equal(0m, Ship(3m, 50m));

        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-amend-diff-");

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void ADiffIsSummarisedByTheLinesItAddsAndRemovesAndHashedWhole()
    {
        AmendDiff diff = AmendDiff.From(Diff, [ShippingFile]);

        Assert.Equal($"+1 -1 lines in {ShippingFile}", diff.Summary);
        Assert.Equal(64, diff.Sha256.Length);
        Assert.Equal(Diff, diff.Text);
    }

    [Fact]
    public void AnAddedLineThatLooksLikeAHeaderStillCounts()
    {
        AmendDiff diff = AmendDiff.From(Diff.Replace("+    public void", "+++ public void", StringComparison.Ordinal), [ShippingFile]);

        Assert.Equal($"+1 -1 lines in {ShippingFile}", diff.Summary);
    }

    [Fact]
    public void ALongDiffIsCutButItsHashCoversAllOfIt()
    {
        string longDiff = "@@ -0,0 +1,500 @@\n" + string.Concat(Enumerable.Range(0, 500).Select(line => $"+line {line}\n"));

        AmendDiff diff = AmendDiff.From(longDiff, [ShippingFile]);

        Assert.Equal(AmendDiff.From(longDiff, [ShippingFile]).Sha256, diff.Sha256);
        Assert.True(diff.Text.Split('\n').Length <= AmendDiff.MaxLines + 2);
        Assert.Contains("more lines; the hash covers the whole diff", diff.Text, StringComparison.Ordinal);
        Assert.Equal($"+500 -0 lines in {ShippingFile}", diff.Summary);
    }

    [Fact]
    public void StatusTraceAndThePullRequestShowTheDiff()
    {
        AmendDiff diff = AmendDiff.From(Diff + "```\n", [ShippingFile]);
        RedGreenRecord record = Record(diff);

        string line = Assert.Single(RedGreenAmends.Lines(record));
        IReadOnlyList<string> pullRequest = ReviewSummary.Amends(RedGreenAmends.Lines(record), RedGreenAmends.Diffs(record));

        Assert.Contains($"diff since the red: +1 -1 lines in {ShippingFile} (sha256 {diff.Sha256[..12]})", line, StringComparison.Ordinal);
        Assert.Contains("<summary>Diff of ShippingTests.FreeShippingStartsAtFifty since its red</summary>", pullRequest);
        Assert.Contains("````diff", pullRequest);
        Assert.Contains("+    public void FreeShippingStartsAtFifty() => Assert.Equal(0m, Ship(3m, 50m));", pullRequest);
    }

    [Fact]
    public void TheDiffRoundTripsInTheRecord()
    {
        AmendDiff diff = AmendDiff.From(Diff, [ShippingFile]);
        Record(diff).Save(RedGreenRecord.PathFor(Feature), Feature.Id);

        RedGreenAmend read = Assert.Single(RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!.Amends);

        Assert.Equal(diff, read.Diff);
    }

    [Fact]
    public async Task TheDiffSinceTheRedComparesTheSnapshotWithTheWorkingTree()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        Write(ShippingFile, "Assert.Equal(0m, Ship(3m, 49m));\n");
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(root.FullName, [root.FullName], TimeSpan.FromSeconds(60), cancellationToken);
        Write(ShippingFile, "Assert.Equal(0m, Ship(3m, 50m));\n");

        string? diff = await RedSnapshots.DiffAsync(root.FullName, snapshot.Commit!, [ShippingFile], TimeSpan.FromSeconds(60), cancellationToken);

        Assert.Contains($"--- a/{ShippingFile}", diff, StringComparison.Ordinal);
        Assert.Contains("-Assert.Equal(0m, Ship(3m, 49m));", diff, StringComparison.Ordinal);
        Assert.Contains("+Assert.Equal(0m, Ship(3m, 50m));", diff, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static RedGreenRecord Record(AmendDiff diff)
    {
        RedGreenAmend amend = new(
            "T:Shop.Tests.ShippingTests.FreeShippingStartsAtFifty",
            "Shop.Tests.ShippingTests.FreeShippingStartsAtFifty",
            [ShippingFile],
            "person@example.com",
            DateTimeOffset.UnixEpoch,
            "free shipping starts at 50.00")
        {
            RecheckedAt = DateTimeOffset.UnixEpoch,
            Diff = diff,
        };
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null,
            [new RedGreenTest(amend.TestKey, amend.MethodFilter, RedClass.RedAssert, ["FR-003"])]) { Amends = [amend] });
        return record;
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
