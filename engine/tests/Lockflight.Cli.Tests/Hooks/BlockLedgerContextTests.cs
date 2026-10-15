using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class BlockLedgerContextTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-blockctx-");

    [Theory]
    [InlineData(null, "main")]
    [InlineData("code-reviewer", "code-reviewer")]
    public async Task ABlockRecordsTheToolTheCallTheSessionAndTheAgent(string? agentType, string expectedAgent)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Dictionary<string, object?> payload = new()
        {
            ["cwd"] = root.FullName,
            ["session_id"] = "s-1",
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = "echo x >> lockflight.yaml" },
        };
        if (agentType is not null)
        {
            payload["agent_type"] = agentType;
        }

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], JsonSerializer.Serialize(payload), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        string line = File.ReadAllLines(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "ledger"))[0]).Last();
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement record = document.RootElement;
        Assert.Equal("Bash", record.GetProperty("tool").GetString());
        Assert.Equal("echo x >> lockflight.yaml", record.GetProperty("call").GetString());
        Assert.Equal("s-1", record.GetProperty("session").GetString());
        Assert.Equal(expectedAgent, record.GetProperty("agent").GetString());
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
