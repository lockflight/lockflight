using Lockflight.Cli.Adoption;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class ClaudeUserSettingsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-claude-settings-");
    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-claude-home-");

    [Fact]
    public void AProjectWithoutClaudeSettingsIsCiOnlyAndKeepsThePluginCommands()
    {
        Assert.True(ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName));
        Assert.True(ClaudeUserSettings.PluginCommandsFor(root.FullName));
    }

    [Fact]
    public void TheTeamsOwnSettingsAndSandboxAreStillCiOnly()
    {
        Write(".claude/settings.json", """{ "permissions": { "deny": ["Read(./.env)"] }, "sandbox": { "enabled": true, "filesystem": { "denyWrite": ["./secrets"] } } }""");

        Assert.True(ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName));
    }

    [Theory]
    [InlineData(".claude/settings.json", """{ "permissions": { "deny": ["Edit(/lockflight.yaml)"] } }""")]
    [InlineData(".claude/settings.json", """{ "sandbox": { "filesystem": { "denyWrite": ["./.lockflight/bin"] } } }""")]
    [InlineData(".claude/settings.json", """{ "extraKnownMarketplaces": { "lockflight": { "source": { "source": "github", "repo": "lockflight/lockflight" } } } }""")]
    [InlineData(".claude/settings.json", """{ "enabledPlugins": [] }""")]
    [InlineData(".claude/settings.json", "{ not json")]
    [InlineData(".claude/settings.local.json", """{ "enabledPlugins": { "lockflight@lockflight": true } }""")]
    public void AnythingLockflightWritesOrUnreadableSettingsAreNotCiOnly(string path, string content)
    {
        Write(path, content);

        Assert.False(ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName));
    }

    [Fact]
    public void ALockflightBlockInClaudeMdIsNotCiOnly()
    {
        Write(ClaudeMdBlock.FileName, ClaudeMdBlock.Text);

        Assert.False(ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName));
    }

    [Fact]
    public void ThePluginEnabledForTheUserIsNotCiOnlyUnlessTheProjectTurnsItOffLocally()
    {
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(home.FullName, ".claude")).FullName, "settings.json"), """{ "enabledPlugins": { "lockflight@lockflight": true } }""");
        bool enabled = ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName);
        Write(".claude/settings.local.json", """{ "enabledPlugins": { "lockflight@lockflight": false } }""");

        Assert.False(enabled);
        Assert.True(ClaudeUserSettings.CiOnlyFor(root.FullName, home.FullName));
    }

    [Fact]
    public void NativeHooksWithThePluginTurnedOffLocallyHaveNoPluginCommands()
    {
        NativeLevelSettings.Write(root.FullName, pluginEnabled: true);
        bool enabled = ClaudeUserSettings.PluginCommandsFor(root.FullName);
        Write(".claude/settings.local.json", """{ "enabledPlugins": { "lockflight@lockflight": false } }""");

        Assert.True(enabled);
        Assert.False(ClaudeUserSettings.PluginCommandsFor(root.FullName));
    }

    [Fact]
    public void UnreadableSettingsKeepThePluginCommands()
    {
        Write(".claude/settings.json", "{ not json");

        Assert.True(ClaudeUserSettings.PluginCommandsFor(root.FullName));
    }

    public void Dispose()
    {
        root.Delete(recursive: true);
        home.Delete(recursive: true);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
