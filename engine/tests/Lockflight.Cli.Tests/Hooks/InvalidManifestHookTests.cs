using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class InvalidManifestHookTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-invalid-");

    public InvalidManifestHookTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 2\nprotected:\n  - secrets/**\nunknown: 1\n");

    [Fact]
    public async Task WithAnInvalidManifestWritesAreBlockedWithTheParseError()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("lockflight.yaml:4:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnInvalidManifestReadingIsStillAllowed()
    {
        string payload = HookPayloads.ToolCall("Read", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Theory]
    [InlineData("version: 1\nprotected:\n  - secrets/**\n", "lockflight.yaml:1:10")]
    [InlineData("protected:\n  - secrets/**\n", "lockflight.yaml:1:1")]
    public async Task AVersionOneManifestBlocksWritesWithTheMigrationCommand(string manifest, string position)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), manifest);
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(position, error, StringComparison.Ordinal);
        Assert.Contains("version 1 manifests are no longer read", error, StringComparison.Ordinal);
        Assert.Contains("update --project", error, StringComparison.Ordinal);
        Assert.Single(error.Trim().Split('\n'));
    }

    [Fact]
    public async Task AVersionOneManifestStillAllowsReading()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 1\n");
        string payload = HookPayloads.ToolCall("Read", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Theory]
    [InlineData("gate", "run")]
    [InlineData("trace")]
    [InlineData("doctor")]
    public async Task ACommandOnAVersionOneManifestIsRefusedWithTheMigrationCommand(params string[] args)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(args, string.Empty, projectRoot.FullName);

        Assert.NotEqual(ExitCode.Pass, exitCode);
        Assert.Contains("version 1 manifests are no longer read", output + error, StringComparison.Ordinal);
        Assert.Contains("update --project", output + error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnInvalidManifestATypedApproveIsRefusedNotPassedToTheAgent()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "user-prompt-expansion"], Expansion("lockflight:approve"), projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("/lockflight:approve was not run", error, StringComparison.Ordinal);
        Assert.Contains("lockflight.yaml:4:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnInvalidManifestAnotherTypedCommandStillExpands()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "user-prompt-expansion"], Expansion("commit"), projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);

    private string Expansion(string command) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["hook_event_name"] = "UserPromptExpansion",
        ["cwd"] = projectRoot.FullName,
        ["session_id"] = "s1",
        ["command_name"] = command,
        ["command_source"] = "plugin",
        ["command_args"] = "spec",
        ["prompt"] = $"/{command} spec",
    });
}
