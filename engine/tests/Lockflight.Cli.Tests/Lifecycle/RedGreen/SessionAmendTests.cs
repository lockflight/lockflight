using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class SessionAmendTests : IDisposable
{
    private const string Code = "k7m2qx";
    private const string SecondCode = "p3w9ha";
    private const string Person = "person@example.com";
    private const string ShippingFile = "tests/Shop.Tests/ShippingTests.cs";
    private const string Test = "ShippingTests.FirstKilogramCostsFive";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-amend-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly RandomSource codes = RandomSource.WithCodes(Code, SecondCode);

    public SessionAmendTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f1"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "tests", "Shop.Tests"));
        File.WriteAllText(Path.Combine(root.FullName, ShippingFile), """
            public sealed class ShippingTests
            {
                [Fact]
                public void FirstKilogramCostsFive() => Assert.Equal(5m, Cost(1m));

                [Fact]
                public void EveryKilogramCostsMore() => Assert.Equal(7.5m, Cost(2m));
            }
            """);
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null,
        [
            new RedGreenTest("T:Shop.Tests.ShippingTests.FirstKilogramCostsFive", "Shop.Tests.ShippingTests.FirstKilogramCostsFive", RedClass.RedStub, []),
            new RedGreenTest("T:Shop.Tests.ShippingTests.EveryKilogramCostsMore", "Shop.Tests.ShippingTests.EveryKilogramCostsMore", RedClass.RedStub, []),
        ]));
        record.Save(RecordPath, "f1");
    }

    private string RecordPath => RedGreenRecord.PathFor(new Feature("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1")));

    [Fact]
    public async Task TheFirstStepShowsTheFileItUnlocksAndTheReasonAndRecordsNothing()
    {
        (ExitCode exitCode, string output, string error) = await TypeAsync($"{Test} --reason \"the rate is 5.50\"");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("Step 1 of 2 — nothing has been amended yet.", error, StringComparison.Ordinal);
        Assert.Contains("Test: Shop.Tests.ShippingTests.FirstKilogramCostsFive", error, StringComparison.Ordinal);
        Assert.Contains($"Unlocks: {ShippingFile}", error, StringComparison.Ordinal);
        Assert.Contains("Also writable, in the same file: Shop.Tests.ShippingTests.EveryKilogramCostsMore", error, StringComparison.Ordinal);
        Assert.Contains("Reason: the rate is 5.50", error, StringComparison.Ordinal);
        Assert.Contains($"Type within 10 minutes: /lockflight:amend {Test} {Code}", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RecordPath)!.Amends);
    }

    [Fact]
    public async Task TheSecondStepRecordsTheAmendWithTheReasonShown()
    {
        await GitAsync("init", "-q");
        await GitAsync("config", "user.email", Person);
        await TypeAsync($"{Test} --reason \"the rate is 5.50\"");

        (ExitCode exitCode, string output, string error) = await TypeAsync($"{Test} {Code}");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("Step 2 of 2 — amended: Shop.Tests.ShippingTests.FirstKilogramCostsFive", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.Contains("redgreen red", output, StringComparison.Ordinal);
        RedGreenAmend amend = Assert.Single(RedGreenRecord.Load(RecordPath)!.Amends);
        Assert.Equal((Person, "the rate is 5.50", time.Now), (amend.By, amend.Reason, amend.At));
        Assert.Equal([ShippingFile], TestLock.AmendedFiles(new Manifest(root.FullName, [], StopGate: false, StopTarget: null, GateSettings.Default)));
    }

    [Fact]
    public async Task ACodeShownForAnotherTestAmendsNothing()
    {
        await TypeAsync($"{Test} --reason x");

        (ExitCode exitCode, _, string error) = await TypeAsync($"EveryKilogramCostsMore {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was amended: the code was shown for T:Shop.Tests.ShippingTests.FirstKilogramCostsFive", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RecordPath)!.Amends);
    }

    [Fact]
    public async Task EvidenceChangedBetweenTheStepsAmendsNothing()
    {
        await TypeAsync($"{Test} --reason x");
        File.AppendAllText(RecordPath, " ");

        (ExitCode exitCode, _, string error) = await TypeAsync($"{Test} {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("changed since you saw them", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RecordPath)!.Amends);
    }

    [Theory]
    [InlineData(Test)]
    [InlineData(Test + " --reason")]
    [InlineData(Test + " " + Code + " --reason x")]
    [InlineData("--reason x")]
    public async Task AnAmendWithoutAReasonOrWithBothAReasonAndACodeIsAUsageRefusal(string arguments)
    {
        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was amended", error, StringComparison.Ordinal);
        Assert.Contains("redgreen amend", error, StringComparison.Ordinal);
        Assert.Empty(RedGreenRecord.Load(RecordPath)!.Amends);
    }

    [Fact]
    public async Task TheCommandAloneWhileACodeWaitsSaysWhichLineToType()
    {
        await TypeAsync($"{Test} --reason x");

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains($"Step 2 of 2 — type: /lockflight:amend {Test} <the code shown with it>", error, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATestOutsideTheOpenCycleIsRefused()
    {
        (ExitCode exitCode, _, string error) = await TypeAsync("Other.Test --reason x");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("'Other.Test' is not a test of red cycle 1", error, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(string arguments) =>
        SessionTyping.TypeAsync(root.FullName, time, codes, SessionAmend.CommandName, arguments);

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
}
