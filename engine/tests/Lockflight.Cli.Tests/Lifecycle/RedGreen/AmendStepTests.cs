using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class AmendStepTests : IDisposable
{
    private const string Person = "person@example.com";
    private const string ShippingFile = "tests/Shop.Tests/ShippingTests.cs";

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 30, 0, TimeSpan.Zero);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-amend-");

    public AmendStepTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Feature.SpecDirectory);
        File.WriteAllText(Feature.SpecPath, "- **FR-001**: A\n");
        File.WriteAllText(Feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n");
        ApprovalStore approvals = new(Feature);
        approvals.Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        approvals.Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "Shop.Tests"));
        File.WriteAllText(Path.Combine(root.FullName, ShippingFile), """
            namespace Shop.Tests;

            public sealed class ShippingTests
            {
                [Fact]
                public void FirstKilogramCostsFive() => Assert.Equal(5m, new PriceCalculator().ShippingCost(1m));

                [Fact]
                public void EveryStartedKilogramCostsTwoFifty() => Assert.Equal(10m, new PriceCalculator().ShippingCost(2.1m));
            }
            """);
        File.WriteAllText(Path.Combine(root.FullName, "tests", "Shop.Tests", "FreeShippingTests.cs"), """
            namespace Shop.Tests;

            public sealed class FreeShippingTests
            {
                [Fact]
                public void OrdersOfFiftyShipFree() => Assert.Equal(0m, new PriceCalculator().ShippingCost(3m));
            }
            """);
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public async Task AnAmendUnlocksTheFileThatDeclaresTheTestAndRecordsWhoWhenAndWhy()
    {
        await GitAsync("init", "-q");
        await GitAsync("config", "user.email", Person);
        SaveRecord(openCycle: true);

        (ExitCode exitCode, string output, string error) = await RunAsync("redgreen", "amend", "ShippingTests.FirstKilogramCostsFive", "--reason", "the rate is 5.50", "--feature", "f1");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains($"{ShippingFile} (the file declaring Shop.Tests.ShippingTests.FirstKilogramCostsFive) becomes writable", output, StringComparison.Ordinal);
        Assert.Contains("these tests of the cycle in it become writable too: Shop.Tests.ShippingTests.EveryStartedKilogramCostsTwoFifty", output, StringComparison.Ordinal);
        RedGreenAmend amend = Assert.Single(RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!.OpenCycle!.Amends);
        Assert.Equal(("T:Shop.Tests.ShippingTests.FirstKilogramCostsFive", Person, Now, "the rate is 5.50"), (amend.TestKey, amend.By, amend.At, amend.Reason));
        Assert.Equal([ShippingFile], amend.Files);
        Assert.True(amend.IsOpen);
        Assert.Equal([ShippingFile], TestLock.AmendedFiles(ProjectManifest()));
        string ledger = string.Concat(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "ledger")).Select(File.ReadAllText));
        Assert.Contains("redgreen:amended", ledger, StringComparison.Ordinal);
        Assert.Contains("the rate is 5.50", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusShowsAnOpenAmendAndItsFile()
    {
        SaveRecord(openCycle: true);
        await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "the rate is 5.50", "--feature", "f1");

        (_, string status, _) = await RunAsync("redgreen", "status", "--feature", "f1");

        Assert.Contains($"RED (test projects locked); amended, writable until the next red: {ShippingFile}", status, StringComparison.Ordinal);
        Assert.Contains("amended: ShippingTests.FirstKilogramCostsFive in cycle 1, by", status, StringComparison.Ordinal);
        Assert.Contains("the rate is 5.50 (waiting for its red)", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendNeedsAReason()
    {
        SaveRecord(openCycle: true);

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--feature", "f1");

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--reason is required", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!.Amends);
    }

    [Fact]
    public async Task AnAmendNamesATest()
    {
        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "--reason", "x");

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("name the test to amend", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendIsRefusedInsideAClaudeCodeSession()
    {
        SaveRecord(openCycle: true);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x"], "", root.FullName, new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("'redgreen amend' records a human decision", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!.Amends);
    }

    [Fact]
    public async Task AnAmendIsRefusedWithoutAnOpenCycle()
    {
        SaveRecord(openCycle: false);

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("no red cycle is open, so the test projects are not locked", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendIsRefusedWithoutEvidence()
    {
        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("no red/green evidence", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendOfATestOutsideTheOpenCycleIsRefusedWithTheCycleTests()
    {
        SaveRecord(openCycle: true);

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "OrdersOfFiftyShipFree", "--reason", "x", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("'OrdersOfFiftyShipFree' is not a test of red cycle 1", error, StringComparison.Ordinal);
        Assert.Contains("Shop.Tests.ShippingTests.FirstKilogramCostsFive", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendThatNamesSeveralTestsIsRefused()
    {
        SaveRecord(openCycle: true, extraTest: new RedGreenTest("T:Shop.Tests.Other.FirstKilogramCostsFive", "Shop.Tests.Other.FirstKilogramCostsFive", RedClass.RedStub, []));

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("names 2 tests of red cycle 1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendIsRefusedWhenNoFileDeclaresTheTest()
    {
        SaveRecord(openCycle: true, extraTest: new RedGreenTest("T:Shop.Tests.Gone.Vanished", "Shop.Tests.Gone.Vanished", RedClass.RedStub, []));

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "Gone.Vanished", "--reason", "x", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("no C# file under the test projects (tests/Shop.Tests) declares Shop.Tests.Gone.Vanished", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondAmendOfTheSameTestIsRefusedWhileTheFirstWaits()
    {
        SaveRecord(openCycle: true);
        await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "y", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("is already amended", error, StringComparison.Ordinal);
        Assert.Single(RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!.Amends);
    }

    [Fact]
    public async Task AGreenIsRefusedWhileAnAmendWaitsForItsRed()
    {
        SaveRecord(openCycle: true);
        await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");

        (ExitCode exitCode, _, string error) = await RunAsync("redgreen", "green", "--feature", "f1");

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("Shop.Tests.ShippingTests.FirstKilogramCostsFive was amended and not re-checked", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReCheckedAmendNoLongerUnlocksItsFile()
    {
        SaveRecord(openCycle: true);
        await RunAsync("redgreen", "amend", "FirstKilogramCostsFive", "--reason", "x", "--feature", "f1");
        RedGreenRecord record = RedGreenRecord.Load(RedGreenRecord.PathFor(Feature))!;
        RedGreenCycle open = record.OpenCycle!;
        record.Cycles[0] = open with { Amends = [.. open.Amends.Select(amend => amend with { RecheckedAt = Now })] };
        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);

        Assert.Empty(TestLock.AmendedFiles(ProjectManifest()));
        (_, string status, _) = await RunAsync("redgreen", "status", "--feature", "f1");
        Assert.Contains("(red again on 2026-10-27 09:30 UTC)", status, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(params string[] args) =>
        TestConsole.RunAsync(args, "", root.FullName, new ManualTime(Now));

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);

    private Manifest ProjectManifest() => new(root.FullName, [], StopGate: false, StopTarget: null, GateSettings.Default);

    private void SaveRecord(bool openCycle, RedGreenTest? extraTest = null)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:Shop.Tests.FreeShippingTests.OrdersOfFiftyShipFree" },
            TestProjects = ["tests/Shop.Tests"],
        };
        RedGreenTest[] tests =
        [
            new("T:Shop.Tests.ShippingTests.FirstKilogramCostsFive", "Shop.Tests.ShippingTests.FirstKilogramCostsFive", RedClass.RedStub, ["FR-001"]),
            new("T:Shop.Tests.ShippingTests.EveryStartedKilogramCostsTwoFifty", "Shop.Tests.ShippingTests.EveryStartedKilogramCostsTwoFifty", RedClass.RedStub, ["FR-001"]),
            .. extraTest is null ? [] : new[] { extraTest },
        ];
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, openCycle ? null : DateTimeOffset.UnixEpoch, tests));
        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);
    }
}
