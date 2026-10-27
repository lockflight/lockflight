using Lockflight.Cli.Config;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RedGreenRecordTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-redgreen-");

    [Fact]
    public void AnOpenCycleRoundTripsAndLocksTheTestProjects()
    {
        string path = Path.Combine(root.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName);
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, ["FR-001"])]));
        record.Save(path, "f1");

        RedGreenRecord loaded = RedGreenRecord.Load(path)!;
        Assert.Equal(1, loaded.OpenCycle!.Number);
        Assert.Equal(["FR-001"], loaded.OpenCycle.Tests[0].Requirements);
        Assert.Equal(["T:A.B", "T:A.C"], loaded.KnownTests.Order(StringComparer.Ordinal));
        Assert.Equal(("tests/Shop.Tests/**", "red cycle 1 of f1"), Assert.Single(TestLock.LockedGlobs(Manifest())));

        loaded.Cycles[0] = loaded.Cycles[0] with { GreenAt = DateTimeOffset.UnixEpoch };
        loaded.Save(path, "f1");
        Assert.Empty(TestLock.LockedGlobs(Manifest()));
    }

    [Fact]
    public void AnAssertionFreeTestRoundTripsAndOtherTestsReadAsAsserting()
    {
        string path = Path.Combine(root.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName);
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null,
            [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, []) { AssertionFree = true }, new RedGreenTest("T:A.D", "A.D", RedClass.RedStub, [])]));
        record.Save(path, "f1");

        RedGreenRecord loaded = RedGreenRecord.Load(path)!;
        Assert.Equal([true, false], loaded.Cycles[0].Tests.Select(test => test.AssertionFree));
        Assert.DoesNotContain("\"assertionFree\": false", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBaselineExplicitTestsRoundTrip()
    {
        string path = Path.Combine(root.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName);
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B", "T:A.Manual" },
            BaselineExplicit = new HashSet<string> { "T:A.Manual" },
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Save(path, "f1");

        Assert.Equal(["T:A.Manual"], RedGreenRecord.Load(path)!.BaselineExplicit);
    }

    [Fact]
    public void ARecordWrittenBeforeExplicitTrackingReadsAsNoExplicitTests()
    {
        string path = Path.Combine(root.FullName, RedGreenRecord.FileName);
        File.WriteAllText(path, """{"baseline":{"commit":"abc","at":"2026-10-11T00:00:00Z","tests":["T:A.B"],"skipped":[]},"testProjects":["tests"],"cycles":[]}""");

        Assert.Empty(RedGreenRecord.Load(path)!.BaselineExplicit);
    }

    [Fact]
    public void AnUnreadableRecordLocksTheConventionalTestFolders()
    {
        string path = Path.Combine(root.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Contains(TestLock.LockedGlobs(Manifest()), locked => locked.Glob == "tests/**");
    }

    [Fact]
    public void AnUnreadableRecordLocksTheTestFoldersOfEveryUnit()
    {
        string path = Path.Combine(root.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Manifest workspace = Manifest() with { Units = [new Unit("services/orders", Build: null, UnitKind.Auto, TestFramework: null, StopFilter: null)] };

        IReadOnlyList<string> globs = [.. TestLock.LockedGlobs(workspace).Select(locked => locked.Glob)];

        Assert.Contains("services/orders/tests/**", globs);
        Assert.Contains("services/orders/test/**", globs);
    }

    private Manifest Manifest() => new(root.FullName, [], StopGate: false, StopTarget: null, GateSettings.Default);

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
