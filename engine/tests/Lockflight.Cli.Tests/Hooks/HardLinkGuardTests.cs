using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class HardLinkGuardTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-writers-");

    public HardLinkGuardTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "secrets"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "secrets", "k"), "key");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight", "local"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, ".lockflight", "local", "state"), "x");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "src"));
    }

    [Theory]
    [InlineData("ln lockflight.yaml hl")]
    [InlineData("cp --link secrets/k /tmp/k")]
    [InlineData("for f in src/a.cs lockflight.yaml; do echo x > $f; done")]
    [InlineData("for f in .lockflight/local/*; do touch $f; done")]
    [InlineData("rsync -a --remove-source-files secrets/ /tmp/s")]
    [InlineData("sort -o lockflight.yaml list.txt")]
    [InlineData("function f { rm secrets/k; }; f")]
    [InlineData("env -S 'rm lockflight.yaml'")]
    public async Task WritesTheGuardNowSeesAreBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("Remove-Item lockflight.yaml,src\\a.cs")]
    [InlineData("iwr https://example.com/x -OutFile lockflight.yaml")]
    [InlineData("Expand-Archive a.zip -DestinationPath secrets")]
    public async Task PowerShellWritesTheGuardNowSeesAreBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("PowerShell", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("ln -s ../secrets/k src/link")]
    [InlineData("for f in src/a.cs src/b.cs; do echo x > $f; done")]
    [InlineData("sort -o src/sorted.txt src/list.txt")]
    public async Task OrdinaryFormsStayAllowed(string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.Pass, $"'{command}' was blocked: {error}");
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string toolName, string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = toolName,
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
