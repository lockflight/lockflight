using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Tests.Init;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class TestFileHashesTests : IDisposable
{
    private const string Project = "tests/Shop.Tests";
    private const string ShippingFile = "tests/Shop.Tests/ShippingTests.cs";
    private const string HelperFile = "tests/Shop.Tests/Helpers/Prices.cs";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-test-hashes-");

    public TestFileHashesTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Write(ShippingFile, "public sealed class ShippingTests { [Fact] public void FirstKilogramCostsFive() {} }\n");
        Write(HelperFile, "static class Prices {}\n");
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project />\n");
        Write("src/Shop.Api/PriceCalculator.cs", "class PriceCalculator {}\n");
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public void EveryFileOfTheTestProjectsIsHashedButNotBuildOutputOrSnapshotTestResults()
    {
        Write("tests/Shop.Tests/bin/Debug/Shop.Tests.dll", "binary");
        Write("tests/Shop.Tests/obj/project.assets.json", "{}");
        Write("tests/Shop.Tests/TestResults/run.trx", "<trx />");
        Write("tests/Shop.Tests/Snapshots/Order.received.txt", "received");
        Write("tests/Shop.Tests/Snapshots/Order.verified.txt", "verified");

        IReadOnlyDictionary<string, string> hashes = TestFileHashes.Compute(root.FullName, [Project]);

        Assert.Equal(
            [HelperFile, ShippingFile, "tests/Shop.Tests/Shop.Tests.csproj", "tests/Shop.Tests/Snapshots/Order.verified.txt"],
            hashes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(64, hashes[ShippingFile].Length);
    }

    [Fact]
    public void EditorFilesAreLeftOutButCompiledSourcesInOutputFoldersAreHashed()
    {
        Write("tests/Shop.Tests/.DS_Store", "finder");
        Write("tests/Shop.Tests/Shop.Tests.csproj.user", "<Project />");
        Write("tests/Shop.Tests/.ShippingTests.cs.swp", "vim");
        Write("tests/Shop.Tests/Thumbs.db", "explorer");
        Write("tests/Shop.Tests/Rates.received.cs", "class Rates {}");
        Write("tests/Shop.Tests/TestResults/Hidden.cs", "class Hidden {}");

        IReadOnlyDictionary<string, string> hashes = TestFileHashes.Compute(root.FullName, [Project]);

        Assert.Equal(
            [HelperFile, "tests/Shop.Tests/Rates.received.cs", ShippingFile, "tests/Shop.Tests/Shop.Tests.csproj", "tests/Shop.Tests/TestResults/Hidden.cs"],
            hashes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnreadableLinkDoesNotStopTheHashing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.CreateSymbolicLink(Path.Combine(root.FullName, "tests", "Shop.Tests", "Dangling.cs"), Path.Combine(root.FullName, "missing.cs"));

        IReadOnlyDictionary<string, string> hashes = TestFileHashes.Compute(root.FullName, [Project]);

        Assert.Contains("tests/Shop.Tests/Dangling.cs", hashes.Keys);
    }

    [Fact]
    public void ATestProjectAtTheRootIsNotHashed() => Assert.Empty(TestFileHashes.Compute(root.FullName, ["."]));

    [Fact]
    public void ChangedAddedAndDeletedFilesAreNamed()
    {
        IReadOnlyDictionary<string, string> atRed = TestFileHashes.Compute(root.FullName, [Project]);
        Write(ShippingFile, "public sealed class ShippingTests { [Fact] public void FirstKilogramCostsFive() { Assert.True(true); } }\n");
        File.Delete(Path.Combine(root.FullName, HelperFile));
        Write("tests/Shop.Tests/Extra.cs", "class Extra {}\n");
        Write("src/Shop.Api/PriceCalculator.cs", "class PriceCalculator { int Price() => 1; }\n");

        TestFileChanges changes = TestFileHashes.Compare(atRed, TestFileHashes.Compute(root.FullName, [Project]));

        Assert.Equal([ShippingFile], changes.Changed);
        Assert.Equal(["tests/Shop.Tests/Extra.cs"], changes.Added);
        Assert.Equal([HelperFile], changes.Deleted);
        Assert.Equal(
            "tests/Shop.Tests/ShippingTests.cs (changed), tests/Shop.Tests/Extra.cs (added), tests/Shop.Tests/Helpers/Prices.cs (deleted)",
            changes.Describe());
    }

    [Fact]
    public void ALineEndingConversionIsNoChange()
    {
        IReadOnlyDictionary<string, string> atRed = TestFileHashes.Compute(root.FullName, [Project]);
        Write(HelperFile, "static class Prices {}\r\n");

        Assert.False(TestFileHashes.Compare(atRed, TestFileHashes.Compute(root.FullName, [Project])).Any);
    }

    [Fact]
    public void UnchangedFilesAreNoChange()
    {
        IReadOnlyDictionary<string, string> atRed = TestFileHashes.Compute(root.FullName, [Project]);

        Assert.False(TestFileHashes.Compare(atRed, TestFileHashes.Compute(root.FullName, [Project])).Any);
    }

    [Fact]
    public void ARecordedHashRoundTripsAndAnOlderRecordHasNone()
    {
        string path = RedGreenRecord.PathFor(Feature);
        SaveRecord(TestFileHashes.Compute(root.FullName, [Project]));
        RedGreenRecord loaded = RedGreenRecord.Load(path)!;
        Assert.Equal(TestFileHashes.Compute(root.FullName, [Project]), loaded.OpenCycle!.TestFiles);

        SaveRecord(new Dictionary<string, string>());
        Assert.Empty(RedGreenRecord.Load(path)!.OpenCycle!.TestFiles);
        Assert.DoesNotContain("testFiles", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GreenIsRefusedWhenATestFileChangedAfterRedWithoutAnAmend()
    {
        Approve();
        SaveRecord(TestFileHashes.Compute(root.FullName, [Project]));
        Write(HelperFile, "static class Prices { public const decimal Rate = 0m; }\n");

        (ExitCode exitCode, string output, _) = await RunAsync("redgreen", "green", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains($"{HelperFile} (changed) changed after red cycle 1 was accepted, without a recorded amend", output, StringComparison.Ordinal);
        Assert.Contains("/lockflight:amend", output, StringComparison.Ordinal);
        Assert.Contains("redgreen amend", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AtTheNativeLevelTheAmendIsHandedToATerminalOnly()
    {
        Approve();
        SaveRecord(TestFileHashes.Compute(root.FullName, [Project]));
        Write(HelperFile, "static class Prices { public const decimal Rate = 0m; }\n");
        NativeLevelSettings.Write(root.FullName, pluginEnabled: false);

        (_, string output, _) = await RunAsync("redgreen", "green", "--feature", "f1");

        Assert.Contains("redgreen amend <Class.Method>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("/lockflight:amend", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GreenWithUnchangedTestFilesGoesOnToTheBuild()
    {
        Approve();
        SaveRecord(TestFileHashes.Compute(root.FullName, [Project]));

        (_, string output, string error) = await RunAsync("redgreen", "green", "--feature", "f1");

        Assert.DoesNotContain("changed after red", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReviewIsRefusedWhenATestFileChangedAfterRedWithoutAnAmend()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: A\n");
        SaveRecord(TestFileHashes.Compute(root.FullName, [Project]));
        Write(ShippingFile, "public sealed class ShippingTests {}\n");

        (ExitCode exitCode, _, string error) = await RunAsync("review", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains($"{ShippingFile} (changed) changed after red cycle 1 was accepted, without a recorded amend", error, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(params string[] args) =>
        TestConsole.RunAsync(args, "", root.FullName, new ManualTime(DateTimeOffset.UnixEpoch));

    private void Approve()
    {
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: A\n");
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n");
        ApprovalStore approvals = new(Feature);
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
    }

    private void SaveRecord(IReadOnlyDictionary<string, string> testFiles)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = [Project],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null,
            [new RedGreenTest("T:Shop.Tests.ShippingTests.FirstKilogramCostsFive", "Shop.Tests.ShippingTests.FirstKilogramCostsFive", RedClass.RedStub, ["FR-001"])])
        {
            TestFiles = testFiles,
        });
        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
