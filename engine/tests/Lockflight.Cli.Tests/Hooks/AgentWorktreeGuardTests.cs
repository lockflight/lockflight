using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class AgentWorktreeGuardTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-worktree-");

    private readonly string worktree;

    public AgentWorktreeGuardTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        worktree = Path.Combine(root.FullName, ".claude", "worktrees", "agent-1");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: ../../../.git/worktrees/agent-1\n");
        File.WriteAllText(Path.Combine(worktree, Manifest.FileName), "version: 1\n");
    }

    [Theory]
    [InlineData("lockflight.yaml")]
    [InlineData(".lockflight/features/001-x/redgreen.json")]
    [InlineData(".claude/settings.json")]
    [InlineData("global.json")]
    public async Task AWorktreesCopyOfAControlFileIsProtected(string relative) =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Write", Path.Combine(worktree, relative), command: null));

    [Fact]
    public async Task AShellWriteFromInsideTheWorktreeIsCheckedAgainstItsRoot() =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Bash", filePath: null, "echo x >> lockflight.yaml"));

    [Fact]
    public async Task OrdinaryCodeInTheWorktreeCanBeEdited() =>
        Assert.Equal(ExitCode.Pass, await GuardAsync("Write", Path.Combine(worktree, "src", "App.cs"), command: null));

    [Fact]
    public async Task AFolderWithoutAGitFileIsNotTreatedAsAWorktree()
    {
        string plain = Path.Combine(root.FullName, ".claude", "worktrees", "notes");
        Directory.CreateDirectory(plain);

        Assert.Equal(ExitCode.Pass, await GuardAsync("Write", Path.Combine(plain, Manifest.FileName), command: null));
    }

    private async Task<ExitCode> GuardAsync(string tool, string? filePath, string? command)
    {
        Dictionary<string, string> toolInput = [];
        if (filePath is not null)
        {
            toolInput["file_path"] = filePath;
            toolInput["content"] = "x";
        }

        if (command is not null)
        {
            toolInput["command"] = command;
        }

        string payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["cwd"] = worktree, ["tool_name"] = tool, ["tool_input"] = toolInput });
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], payload, root.FullName, new Dictionary<string, string> { [EnvironmentVariables.ClaudeProjectDirectory] = root.FullName });
        return exitCode;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
