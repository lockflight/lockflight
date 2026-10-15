using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class GuardScenarioTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-guard-");

    public GuardScenarioTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Theory]
    [InlineData("global.json")]
    [InlineData("lockflight.yaml")]
    [InlineData("src/Directory.Build.props")]
    [InlineData(".claude/settings.json")]
    [InlineData(".editorconfig")]
    [InlineData("src/Shop.Api/.editorconfig")]
    [InlineData("analyzers.globalconfig")]
    [InlineData("src/BannedSymbols.txt")]
    public async Task BuiltInProtectedPathsAreBlockedWithoutBeingListed(string relativePath)
    {
        string payload = HookPayloads.ToolCall("Edit", Path.Combine(projectRoot.FullName, relativePath), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task ABashRedirectIntoAProtectedFileIsBlocked()
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync("echo leaked > secrets/key.txt");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("secrets/**", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentCannotAcceptItsOwnWarningsIntoTheBaseline()
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync("dotnet lockflight baseline build");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("ask the user", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryBuildCommandIsAllowed()
    {
        (ExitCode exitCode, _, _) = await RunBashAsync("dotnet build && dotnet test");

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task InLockdownOnlyReadingIsAllowed()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, LockdownPolicy.RelativeMarkerPath), "");
        string write = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);
        string read = HookPayloads.ToolCall("Read", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode writeExit, _, string writeError) = await TestConsole.RunAsync(["hook", "pre-tool-use"], write, projectRoot.FullName);
        (ExitCode readExit, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], read, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, writeExit);
        Assert.Contains("lockdown", writeError, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, readExit);
    }

    [Fact]
    public async Task AnUnknownHookEventIsBlockedInsideALockflightProject()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "teammate-idle"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Theory]
    [InlineData("(cd /tmp); rm secrets/k")]
    [InlineData("(cd /tmp); echo x > secrets/k")]
    [InlineData("cd /nonexistent-dir; rm secrets/k")]
    [InlineData("cd /tmp | true; rm secrets/k")]
    [InlineData("cd /tmp & rm secrets/k")]
    [InlineData("cd \"$PWD\" && rm secrets/k")]
    [InlineData("cd $(pwd) && rm secrets/k")]
    [InlineData("pushd /tmp; popd; rm secrets/k")]
    [InlineData("cd ~ && cd - && rm secrets/k")]
    [InlineData("cd /tmp && rm secrets/k")]
    public async Task ACdNeverHidesAWriteToTheProtectedPathAsWritten(string command)
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("secrets/**", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cd secrets && rm k")]
    [InlineData("git checkout HEAD -- secrets")]
    [InlineData("git clean -fdx secrets/old")]
    [InlineData("rm -rf secrets")]
    [InlineData("git restore .")]
    public async Task ARewriteOfADirectoryThatCanHoldProtectedFilesIsBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunBashAsync(command);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Theory]
    [InlineData("cd src && echo x > Note.cs")]
    [InlineData("rm -rf src/bin src/obj")]
    [InlineData("dotnet format")]
    [InlineData("git restore src/Api.cs")]
    public async Task OrdinaryWritesOutsideProtectedPathsAreAllowed(string command)
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Theory]
    [InlineData("(cd /tmp); rm tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("cd $(pwd) && rm tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("git checkout HEAD -- tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("git restore tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("git stash push -- tests/Shop.Tests")]
    [InlineData("perl -pi -e 's/5m/6m/' tests/Shop.Tests/ShippingTests.cs")]
    [InlineData("cd tests/Shop.Tests && echo x > ShippingTests.cs")]
    [InlineData("dotnet format")]
    [InlineData("dotnet format tests/Shop.Tests/Shop.Tests.csproj")]
    [InlineData("git restore .")]
    public async Task ShellRewritesOfLockedTestsAreBlockedDuringRed(string command)
    {
        OpenRedCycle();

        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("red cycle 1", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("git restore src/Shop.Api/PriceCalculator.cs")]
    [InlineData("dotnet format src/Shop.Api/Shop.Api.csproj")]
    [InlineData("cd src && echo x > Note.cs")]
    public async Task ShellRewritesOfProductionCodeAreAllowedDuringRed(string command)
    {
        OpenRedCycle();

        (ExitCode exitCode, _, _) = await RunBashAsync(command);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    private void OpenRedCycle()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "tests", "Shop.Tests"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "tests", "Shop.Tests", "ShippingTests.cs"), "class ShippingTests;\n");
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, ["FR-001"])]));
        record.Save(Path.Combine(projectRoot.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName), "f1");
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

    public void Dispose() => projectRoot.Delete(recursive: true);
}
