using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class PreToolUseHookTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-hook-");

    public PreToolUseHookTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), """
            protected:
              - "secrets/**"
              - "*.lock"
            """);
    }

    [Fact]
    public async Task AWriteToAProtectedPathIsBlockedWithTheRuleAsTheReason()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "secrets", "key.txt"), projectRoot.FullName);

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Equal(string.Empty, output);
        Assert.Contains("secrets/**", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWriteToAnUnprotectedPathPassesSilently()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "Program.cs"), projectRoot.FullName);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal(string.Empty, output);
    }

    [Fact]
    public async Task OutsideALockflightProjectTheHookIsANoOp()
    {
        DirectoryInfo unrelated = Directory.CreateTempSubdirectory("lockflight-unrelated-");
        try
        {
            string payload = HookPayloads.ToolCall("Write", Path.Combine(unrelated.FullName, "secrets", "key.txt"), unrelated.FullName);

            (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, unrelated.FullName);

            Assert.Equal(ExitCode.Pass, exitCode);
            Assert.Equal(string.Empty, output);
        }
        finally
        {
            unrelated.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InputThatIsNotJsonIsBlockedRatherThanAllowed()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], "not json", projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task AnEngineThatDiffersFromThePinnedVersionIsBlockedWithTheFix()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"),
            """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "9.9.9", "commands": ["lockflight"] } } }""");
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("9.9.9", error, StringComparison.Ordinal);
        Assert.Contains("dotnet tool restore", error, StringComparison.Ordinal);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
