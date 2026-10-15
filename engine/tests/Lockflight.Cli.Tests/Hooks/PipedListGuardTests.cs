using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class PipedListGuardTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-pipedlist-");

    public PipedListGuardTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "protected:\n  - \"**/.env\"\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "web", "src"));
        File.WriteAllText(Path.Combine(root.FullName, "web", "src", "app.ts"), "Foo\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "api"));
        File.WriteAllText(Path.Combine(root.FullName, "api", ".env"), "KEY=1\n");
    }

    [Fact]
    public async Task AMassEditInATreeWithoutProtectedFilesIsAllowed() =>
        Assert.Equal(ExitCode.Pass, await GuardAsync("grep -rl Foo web | xargs sed -i 's/Foo/Bar/g'"));

    [Fact]
    public async Task AMassEditInATreeHoldingAProtectedFileIsBlocked() =>
        Assert.Equal(ExitCode.HookBlock, await GuardAsync("grep -rl KEY api | xargs sed -i 's/KEY/X/'"));

    private async Task<ExitCode> GuardAsync(string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = root.FullName,
            ["tool_name"] = "Bash",
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, root.FullName);
        return exitCode;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
