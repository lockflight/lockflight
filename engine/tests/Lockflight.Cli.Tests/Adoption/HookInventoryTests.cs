using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class HookInventoryTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight hooks ");

    [Theory]
    [InlineData("$CLAUDE_PROJECT_DIR/.claude/hooks/guard.sh")]
    [InlineData("\"$CLAUDE_PROJECT_DIR\"/.claude/hooks/guard.sh")]
    [InlineData("\"$CLAUDE_PROJECT_DIR/.claude/hooks/guard.sh\"")]
    [InlineData("\"${CLAUDE_PROJECT_DIR}\"/.claude/hooks/guard.sh --strict")]
    [InlineData("bash \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/guard.sh")]
    [InlineData("'$CLAUDE_PROJECT_DIR'/.claude/hooks/guard.sh; echo done")]
    public void TheScriptIsFoundHoweverThePathIsQuoted(string command)
    {
        string script = Path.Combine(root.FullName, ".claude", "hooks", "guard.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, "exit 0\n");
        string settings = Path.Combine(root.FullName, ".claude", "settings.json");
        string json = System.Text.Json.JsonSerializer.Serialize(command);
        File.WriteAllText(settings, "{\"hooks\":{\"PreToolUse\":[{\"matcher\":\"Edit|Write\",\"hooks\":[{\"type\":\"command\",\"command\":" + json + "}]}]}}");

        ClaudeHook hook = Assert.Single(HookInventory.Read(settings, "shell", root.FullName));

        Assert.Equal(script, hook.ScriptPath);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
