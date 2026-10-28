using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class HookRobustnessTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-robust-");

    public HookRobustnessTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Theory]
    [InlineData("pre-tool-use", true)]
    [InlineData("stop", false)]
    [InlineData("session-start", false)]
    public async Task AnEngineErrorBlocksAToolCallButNeverAStopOrSessionStart(string eventName, bool blocks)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new FailingReader(), output, error, projectRoot.FullName, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string>()),
        };

        ExitCode exitCode = await new HookCommand(context).RunAsync(HookEventNames.Parse(eventName), TestContext.Current.CancellationToken);

        Assert.Equal(blocks ? ExitCode.HookBlock : ExitCode.Pass, exitCode);
        Assert.Contains("lockflight: internal error", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHookLogThatCannotBeWrittenDoesNotDecideTheHook()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);
        Dictionary<string, string> environment = new() { [EnvironmentVariables.HookLog] = projectRoot.FullName };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName, environment);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("cannot write the hook log", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[]")]
    [InlineData("""{ "tools": [] }""")]
    [InlineData("""{ "tools": { "lockflight.cli": { "version": 7 } } }""")]
    public async Task AMalformedToolManifestBlocksWithTheRestoreCommand(string toolManifest)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"), toolManifest);
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(RecoveryCommands.RestoreAndLink, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Read", null, false)]
    [InlineData("Grep", null, false)]
    [InlineData("Bash", "dotnet tool restore && dotnet lockflight link", false)]
    [InlineData("Bash", "dotnet tool restore", false)]
    [InlineData("Bash", "dotnet lockflight link", false)]
    [InlineData("Bash", "dotnet tool restore; rm -rf src", true)]
    [InlineData("Bash", "dotnet build", true)]
    [InlineData("Write", null, true)]
    public async Task WithAStaleEngineOnlyReadingAndTheRestoreCommandAreAllowed(string toolName, string? command, bool blocks)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"),
            """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "9.9.9", "commands": ["lockflight"] } } }""");
        string payload = command is null
            ? HookPayloads.ToolCall(toolName, Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName)
            : BashPayload(command);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(blocks ? ExitCode.HookBlock : ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task TheSessionBannerListsTheBuiltInProtectedPathsToo()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("secrets/**", output, StringComparison.Ordinal);
        Assert.Contains("global.json", output, StringComparison.Ordinal);
        Assert.Contains("**/Directory.Build.props", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSessionBannerNamesTheEngineCommandAndWhereHumanDecisionsRun()
    {
        (_, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), projectRoot.FullName);
        string banner = JsonNode.Parse(output)!["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>();

        Assert.Contains($"Always run Lockflight commands as .lockflight/bin/lockflight <command> from {projectRoot.FullName}", banner, StringComparison.Ordinal);
        Assert.Contains("not with `!` in Claude Code", banner, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSessionBannerStaysShortWithAHugeManifest()
    {
        string globs = string.Concat(Enumerable.Range(0, 2000).Select(index => $"  - generated/{index}/**\n"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n" + globs);

        (_, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), projectRoot.FullName);

        Assert.InRange(output.Length, 1, 9000);
        Assert.Contains("more (see lockflight.yaml)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProjectDirectoryComesFromTheInjectedEnvironment()
    {
        DirectoryInfo unrelated = Directory.CreateTempSubdirectory("lockflight-unrelated-");
        try
        {
            string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "secrets", "k"), projectRoot.FullName);
            Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeProjectDirectory] = unrelated.FullName };

            (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName, environment);

            Assert.Equal(ExitCode.Pass, exitCode);
        }
        finally
        {
            unrelated.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AnIncompatiblePluginApiFromTheInjectedEnvironmentBlocks()
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.PluginApi] = "99" };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], BashPayload("true"), projectRoot.FullName, environment);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("hook API 99", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APluginThatNeedsANewerEngineTellsThePersonAtSessionStart()
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.PluginMinimumEngine] = "99.0.0" };

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), projectRoot.FullName, environment);

        Assert.Equal(ExitCode.Pass, exitCode);
        string notice = JsonDocument.Parse(output).RootElement.GetProperty("systemMessage").GetString()!;
        Assert.Contains("needs engine 99.0.0 or newer", notice, StringComparison.Ordinal);
        Assert.Contains("lockflight update", notice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.0.0-alpha")]
    [InlineData("not a version")]
    public async Task APluginSatisfiedByTheEngineAddsNoNotice(string minimum)
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.PluginMinimumEngine] = minimum };

        (_, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), projectRoot.FullName, environment);

        Assert.False(JsonDocument.Parse(output).RootElement.TryGetProperty("systemMessage", out _));
    }

    [Fact]
    public async Task AHumanOnlyCommandIsRefusedWhenTheInjectedEnvironmentSaysAgent()
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeCode] = "1" };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["approve", "spec"], string.Empty, projectRoot.FullName, environment);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("records a human decision", error, StringComparison.Ordinal);
        Assert.Contains("not with `!` in Claude Code", error, StringComparison.Ordinal);
        Assert.Contains($"cd '{projectRoot.FullName}' && dotnet lockflight approve spec", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("approve", "--help")]
    [InlineData("approve", "spec", "--help", "--feature", "001-orders")]
    [InlineData("redgreen", "reset", "-h")]
    [InlineData("baseline", "tests", "--help")]
    public async Task AReservedCommandAskedForHelpPrintsTheUsageUnderTheAgentAndRecordsNothing(params string[] args)
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeCode] = "1" };
        string[] before = [.. Directory.EnumerateFileSystemEntries(projectRoot.FullName, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(args, string.Empty, projectRoot.FullName, environment);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Usage: lockflight <command>", output, StringComparison.Ordinal);
        Assert.Empty(error);
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(projectRoot.FullName, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("lockflight approve --help", true)]
    [InlineData("lockflight approve spec --feature 001-orders", false)]
    public async Task TheGuardLetsTheAgentAskAReservedCommandForHelp(string command, bool allowed)
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], BashPayload(command), projectRoot.FullName);

        Assert.Equal(allowed ? ExitCode.Pass : ExitCode.HookBlock, exitCode);
    }

    private string BashPayload(string command) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["cwd"] = projectRoot.FullName,
        ["tool_name"] = "Bash",
        ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
    });

    public void Dispose() => projectRoot.Delete(recursive: true);

    private sealed class FailingReader : TextReader
    {
        public override Task<string> ReadToEndAsync(CancellationToken cancellationToken) => throw new IOException("stdin broke");
    }
}
