using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class WorkspaceGuardTests : IDisposable
{
    private readonly DirectoryInfo shell = Directory.CreateTempSubdirectory("lockflight-shell-");

    public WorkspaceGuardTests() =>
        File.WriteAllText(Path.Combine(shell.FullName, Manifest.FileName), """
            version: 1
            units:
              - path: services/orders
              - path: web/admin
                kind: none
            """);

    [Theory]
    [InlineData("services/orders/global.json")]
    [InlineData("services/orders/dotnet-tools.json")]
    [InlineData("services/orders/.config/dotnet-tools.json")]
    [InlineData("services/orders/lockflight.yaml")]
    [InlineData("services/orders/.lockflight/features/f1/redgreen.json")]
    [InlineData("services/orders/.claude/settings.json")]
    [InlineData("services/orders/.github/workflows/ci.yml")]
    [InlineData("services/orders/.gitleaks.toml")]
    [InlineData("web/admin/.github/workflows/deploy.yml")]
    [InlineData(".claude/hooks/contracts-guard.sh")]
    [InlineData(".claude/agents/code-reviewer.md")]
    [InlineData(".claude/skills/team-conventions/SKILL.md")]
    [InlineData(".claude/commands/release.md")]
    [InlineData("services/orders/.claude/hooks/guard.sh")]
    public async Task EveryUnitsControlFilesAndTheTeamsOwnControlsAreProtected(string relativePath)
    {
        string payload = HookPayloads.ToolCall("Edit", Path.Combine(shell.FullName, relativePath), shell.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, shell.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Theory]
    [InlineData("services/orders/src/Orders/Order.cs")]
    [InlineData("web/admin/src/invoice.ts")]
    [InlineData("services/billing/global.json")]
    public async Task OrdinaryUnitFilesAndPathsOutsideAnyUnitAreNotTreatedAsUnitControls(string relativePath)
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(shell.FullName, relativePath), shell.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, shell.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task AShellCommandInsideAUnitIsJudgedAgainstThatUnitsControls()
    {
        Directory.CreateDirectory(Path.Combine(shell.FullName, "services", "orders"));
        string payload = BashPayload("cd services/orders && echo '{}' > global.json");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, shell.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("global.json", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionStartedInsideAUnitIsToldThatTheShellsSettingsDidNotLoad()
    {
        string unit = Path.Combine(shell.FullName, "services", "orders");
        Directory.CreateDirectory(unit);
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeProjectDirectory] = unit };

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), unit, environment);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("below the project root", output, StringComparison.Ordinal);
        Assert.Contains("Units: services/orders, web/admin", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASessionStartedAtTheShellRootGetsNoStartDirectoryWarning()
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeProjectDirectory] = shell.FullName };

        (_, string output, _) = await TestConsole.RunAsync(["hook", "session-start"], BashPayload("true"), shell.FullName, environment);

        Assert.DoesNotContain("below the project root", output, StringComparison.Ordinal);
    }

    private string BashPayload(string command) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["hook_event_name"] = "PreToolUse",
            ["cwd"] = shell.FullName,
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });

    public void Dispose() => TempDirectories.DeleteRecursively(shell);
}
