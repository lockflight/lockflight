using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>
/// The guard outside the session's root: a second checkout of a Lockflight project (a git worktree in /tmp, another clone) gets
/// the per-root rules at its own root, and the user's install (~/.lockflight), which the recovery command runs, is never written.
/// </summary>
public sealed class OtherCheckoutGuardTests : IDisposable
{
    private readonly DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-other-checkout-");

    private string Project => Path.Combine(work.FullName, "project");

    private string Other => Path.Combine(work.FullName, "wt-x");

    private string Home => Path.Combine(work.FullName, "home");

    public OtherCheckoutGuardTests()
    {
        foreach (string root in new[] { Project, Other })
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, Manifest.FileName), "version: 1\n");
        }

        Directory.CreateDirectory(Path.Combine(Home, ".lockflight", "bin"));
    }

    [Theory]
    [InlineData("lockflight.yaml")]
    [InlineData(".lockflight/engine.lock")]
    public async Task AnEditInAnotherCheckoutOfTheProjectIsJudgedAtItsRoot(string relative) =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Write", Path.Combine(Other, relative), command: null));

    [Fact]
    public async Task AShellWriteIntoAnotherCheckoutIsJudgedAtItsRoot() =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Bash", filePath: null, $"echo x > {Other}/lockflight.yaml"));

    [Fact]
    public async Task OrdinaryCodeInAnotherCheckoutCanBeEdited() =>
        Assert.Equal(ExitCode.Pass, await GuardAsync("Write", Path.Combine(Other, "src", "App.cs"), command: null));

    [Fact]
    public async Task AFolderOutsideAnyProjectIsNotJudged() =>
        Assert.Equal(ExitCode.Pass, await GuardAsync("Write", Path.Combine(work.FullName, "notes", Manifest.FileName + ".bak"), command: null));

    [Theory]
    [InlineData("cp src/x ~/.lockflight/bin/lockflight")]
    [InlineData("cp src/x $HOME/.lockflight/tools/9.9.9/lockflight")]
    [InlineData("rm -rf ~/.lockflight")]
    [InlineData("rm -rf ~/.lock*")]
    public async Task AShellWriteToTheUsersInstallIsBlocked(string command) =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Bash", filePath: null, command));

    [Fact]
    public async Task AnEditOfTheUsersEngineIsBlocked() =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("Write", Path.Combine(Home, ".lockflight", "bin", "lockflight"), command: null));

    [Fact]
    public async Task TheRecoveryCommandAndOtherWritesInTheHomeFolderAreAllowed()
    {
        Assert.Equal(ExitCode.Pass, await GuardAsync("Bash", filePath: null, "cp src/x ~/notes.txt"));
        Assert.Equal(ExitCode.Pass, await GuardAsync("Bash", filePath: null, "~/.lockflight/bin/lockflight link --installed"));
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

        string payload = JsonSerializer.Serialize(new Dictionary<string, object> { ["cwd"] = Project, ["tool_name"] = tool, ["tool_input"] = toolInput });
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(
            ["hook", "pre-tool-use"], payload, Project,
            new Dictionary<string, string> { [EnvironmentVariables.ClaudeProjectDirectory] = Project, [EnvironmentVariables.Home] = Home, [EnvironmentVariables.UserProfile] = Home });
        return exitCode;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(work);
}
