using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class AmendedTestGuardTests : IDisposable
{
    private const string Amended = "tests/Shop.Tests/ShippingTests.cs";
    private const string Other = "tests/Shop.Tests/FreeShippingTests.cs";

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-amend-guard-");

    public AmendedTestGuardTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "tests", "Shop.Tests"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, Amended), "class ShippingTests;\n");
        File.WriteAllText(Path.Combine(projectRoot.FullName, Other), "class FreeShippingTests;\n");
    }

    [Theory]
    [InlineData("Edit")]
    [InlineData("Write")]
    public async Task TheAmendedTestFileCanBeEditedDuringRed(string tool)
    {
        SaveRecord("f1", rechecked: false);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], HookPayloads.ToolCall(tool, Path.Combine(projectRoot.FullName, Amended), projectRoot.FullName), projectRoot.FullName);

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Theory]
    [InlineData("perl -pi -e 's/5m/6m/' tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("cd tests/Shop.Tests && echo x > ShippingTests.cs")]
    public async Task ANamedShellWriteToTheAmendedFileIsAllowed(string command)
    {
        SaveRecord("f1", rechecked: false);

        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Theory]
    [InlineData("echo x > tests/Shop.Tests/FreeShippingTests.cs")]
    [InlineData("dotnet format")]
    [InlineData("git restore tests/Shop.Tests")]
    public async Task OtherTestFilesAndWholeDirectoriesStayLocked(string command)
    {
        SaveRecord("f1", rechecked: false);

        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("red cycle 1 of f1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditOfAnotherTestFileIsBlockedAndPointsAtAnAmend()
    {
        SaveRecord("f1", rechecked: false);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], HookPayloads.ToolCall("Edit", Path.Combine(projectRoot.FullName, Other), projectRoot.FullName), projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("redgreen amend <test>", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFileIsLockedAgainOnceARedReChecksTheAmend()
    {
        SaveRecord("f1", rechecked: true);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], HookPayloads.ToolCall("Edit", Path.Combine(projectRoot.FullName, Amended), projectRoot.FullName), projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task AnAmendInOneFeatureDoesNotLiftAnotherFeaturesLock()
    {
        SaveRecord("f1", rechecked: false);
        SaveRecord("f2", rechecked: true);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], HookPayloads.ToolCall("Edit", Path.Combine(projectRoot.FullName, Amended), projectRoot.FullName), projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("tests are locked during red cycle 1", error, StringComparison.Ordinal);
        Assert.Empty(TestLock.AmendedFiles(new Manifest(projectRoot.FullName, [], StopGate: false, StopTarget: null, GateSettings.Default)));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(projectRoot);

    private void SaveRecord(string feature, bool rechecked)
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = ["tests/Shop.Tests"],
        };
        RedGreenAmend amend = new("T:Shop.Tests.ShippingTests.A", "Shop.Tests.ShippingTests.A", [Amended], "person@example.com", DateTimeOffset.UnixEpoch, "wrong rate")
        {
            RecheckedAt = rechecked ? DateTimeOffset.UnixEpoch : null,
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest(amend.TestKey, amend.MethodFilter, RedClass.RedStub, [])])
        {
            Amends = [amend],
        });
        record.Save(Path.Combine(projectRoot.FullName, ".lockflight", "features", feature, RedGreenRecord.FileName), feature);
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunBashAsync(string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }
}
