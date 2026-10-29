using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class HookAdapterTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-hook-adapter-");

    public HookAdapterTests() => File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

    [Theory]
    [InlineData("Read", "read", null, false)]
    [InlineData("LS", "read", null, false)]
    [InlineData("Grep", "search", null, false)]
    [InlineData("Glob", "search", null, false)]
    [InlineData("Edit", "edit", null, true)]
    [InlineData("MultiEdit", "edit", null, true)]
    [InlineData("Write", "write", null, true)]
    [InlineData("NotebookEdit", "notebook", null, true)]
    [InlineData("Bash", "shell", "posix", false)]
    [InlineData("PowerShell", "shell", "powershell", false)]
    [InlineData("WebFetch", "fetch", null, false)]
    [InlineData("WebSearch", "fetch", null, false)]
    [InlineData("Task", "agent", null, false)]
    [InlineData("Agent", "agent", null, false)]
    [InlineData("CronCreate", "other", null, false)]
    [InlineData("mcp__files__read", "other", null, false)]
    public void EachClaudeCodeToolMapsToItsKind(string toolName, string kind, string? shell, bool writesFiles)
    {
        HookTool tool = Parse(new { tool_name = toolName, tool_input = new { pattern = "x" } }).Tool;

        Assert.Equal(toolName, tool.Name);
        Assert.Equal(kind, NeutralRequestJson.KindName(tool.Kind));
        Assert.Equal(shell, tool.Shell switch { ShellDialect.Posix => "posix", ShellDialect.PowerShell => "powershell", _ => null });
        Assert.Equal(writesFiles, tool.WritesFiles);
    }

    [Fact]
    public void TheFileAToolNamesBecomesItsPathAndTheRestOfThePayloadItsSession()
    {
        HookRequest request = ClaudeCodeAdapter.Parse(
            JsonSerializer.Serialize(new { tool_name = "NotebookEdit", cwd = "/repo/src", session_id = "s1", agent_type = "test-author", tool_input = new { notebook_path = "/repo/a.ipynb" } }),
            projectDirectory: "/repo")!;

        Assert.Equal(["/repo/a.ipynb"], request.Paths);
        Assert.Equal(new HookSession("s1", "test-author", "/repo"), request.Session);
        Assert.Equal("/repo/src", request.WorkingDirectory);
    }

    [Fact]
    public void AnUnknownToolThatNamesAPathIsAFileWrite()
    {
        HookRequest request = Parse(new { tool_name = "mcp__files__write_file", tool_input = new { path = "/repo/lockflight.yaml", content = "x" } });

        Assert.Equal(new HookTool("mcp__files__write_file", ToolKind.Other, null, WritesFiles: true), request.Tool);
        Assert.Equal(["/repo/lockflight.yaml"], request.Paths);
    }

    [Fact]
    public void ASearchToolsPathIsNotAFileItWrites()
    {
        HookRequest request = Parse(new { tool_name = "Grep", tool_input = new { pattern = "x", path = "/repo" } });

        Assert.Empty(request.Paths);
        Assert.False(request.Tool.WritesFiles);
    }

    [Fact]
    public void APromptExpansionCarriesTheCommand()
    {
        HookRequest request = Parse(new { command_name = "lockflight:approve", command_source = "plugin", command_args = "spec" });

        Assert.Equal(new HookPrompt("lockflight:approve", "plugin", "spec"), request.Prompt);
        Assert.Equal(ToolKind.Other, request.Tool.Kind);
    }

    [Fact]
    public async Task AnUnknownToolWritingAProtectedPathIsBlockedAsAFileWrite()
    {
        (ExitCode exitCode, _, string error) = await RunAsync("pre-tool-use", JsonSerializer.Serialize(new
        {
            tool_name = "mcp__files__write_file",
            cwd = root.FullName,
            tool_input = new { path = Path.Combine(root.FullName, Manifest.FileName), content = "x" },
        }));

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(Manifest.FileName, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANeutralRequestIsJudgedLikeTheClientPayload()
    {
        string request = $$"""
            { "schema": "lockflight.hook-request/v1", "event": "pre-tool",
              "tool": { "name": "apply_patch", "kind": "edit", "shell": null },
              "paths": ["{{JsonEncodedText.Encode(Path.Combine(root.FullName, "src", "A.cs"))}}", "{{JsonEncodedText.Encode(Path.Combine(root.FullName, Manifest.FileName))}}"],
              "command": null, "session": { "id": "s1", "agent": "main", "projectDir": "{{JsonEncodedText.Encode(root.FullName)}}" },
              "cwd": null, "prompt": null, "task": null, "inputStrings": [], "client": { "name": "other", "version": "1.0" } }
            """;

        (ExitCode exitCode, _, string error) = await RunAsync("pre-tool-use", request, NeutralAdapter.Name);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(Manifest.FileName, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "schema": "lockflight.hook-request/v1", "event": "stop", "tool": { "name": "Edit", "kind": "edit" } }""")]
    [InlineData("""{ "schema": "lockflight.hook-request/v2", "event": "pre-tool", "tool": { "name": "Edit", "kind": "edit" } }""")]
    [InlineData("""{ "schema": "lockflight.hook-request/v1", "event": "pre-tool", "tool": { "name": "Edit", "kind": "patch" } }""")]
    [InlineData("""{ "schema": "lockflight.hook-request/v1", "event": "pre-tool", "tool": { "name": "Bash", "kind": "shell" } }""")]
    public async Task ANeutralRequestForAnotherEventSchemaOrKindIsInvalidInput(string request)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("pre-tool-use", request, NeutralAdapter.Name);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("hook input is not valid", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownAdapterBlocksAToolCall()
    {
        (ExitCode exitCode, _, string error) = await RunAsync("pre-tool-use", JsonSerializer.Serialize(new { tool_name = "Read", cwd = root.FullName }), "cursor");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("LOCKFLIGHT_HOOK_ADAPTER='cursor' names no hook adapter", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownAdapterDoesNotBlockStop()
    {
        (ExitCode exitCode, _, _) = await RunAsync("stop", JsonSerializer.Serialize(new { cwd = root.FullName }), "cursor");

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task WithoutTheVariableThePayloadIsClaudeCodes()
    {
        (ExitCode exitCode, _, _) = await RunAsync(
            "pre-tool-use", JsonSerializer.Serialize(new { tool_name = "Edit", cwd = root.FullName, tool_input = new { file_path = Path.Combine(root.FullName, Manifest.FileName) } }));

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static HookRequest Parse(object payload) => ClaudeCodeAdapter.Parse(JsonSerializer.Serialize(payload), projectDirectory: null)!;

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string hookEvent, string payload, string? adapter = null)
    {
        Dictionary<string, string> environment = adapter is null ? [] : new() { [EnvironmentVariables.HookAdapter] = adapter };
        return await TestConsole.RunAsync(["hook", hookEvent], payload, root.FullName, environment);
    }
}
