using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.Updating;

public sealed class UpdateCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-update-");

    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    [Fact]
    public async Task WithoutAnInstallFromSourceItSaysWhatToDoInstead()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["update"], "", root.FullName, Home());

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("dotnet tool update", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentCannotUpdateItsOwnJudge()
    {
        Dictionary<string, string> agent = new(Home()) { [EnvironmentVariables.ClaudeCode] = "1" };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["update", "--project"], "", root.FullName, agent);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("separate terminal window", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectUpdateRepinsLinksAndRefreshesAnOlderBlockOnce()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        File.WriteAllText(Path.Combine(root.FullName, "dotnet-tools.json"), """
            {"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"0.0.1-dev.1","commands":["lockflight"]}}}
            """);
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), $"# Team\n\n{ClaudeMdBlock.Begin}\nold\n{ClaudeMdBlock.End}\n");

        (ExitCode first, string output, _) = await TestConsole.RunAsync(["update", "--project"], "", root.FullName, Home());
        (_, string again, _) = await TestConsole.RunAsync(["update", "--project"], "", root.FullName, Home());

        Assert.Equal(ExitCode.Pass, first);
        Assert.Equal(ToolPin.RunningVersion, ToolPin.ReadPinnedVersion(root.FullName));
        Assert.True(ClaudeMdBlock.IsCurrent(File.ReadAllText(Path.Combine(root.FullName, "CLAUDE.md"))));
        Assert.Contains("updated dotnet-tools.json, CLAUDE.md", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root.FullName, ".lockflight", "bin", OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight")));
        Assert.Contains("already up to date", again, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstalledVersionIsReadFromTheEngineLink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string tool = Path.Combine(home.FullName, ".lockflight", "tools", "0.2.0-dev.20261015000000", "lockflight");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        File.WriteAllText(tool, string.Empty);
        Directory.CreateDirectory(Path.Combine(home.FullName, ".lockflight", "bin"));
        File.CreateSymbolicLink(UserInstall.Engine(home.FullName), tool);

        Assert.Equal("0.2.0-dev.20261015000000", UserInstall.InstalledVersion(home.FullName));
        Assert.Null(UserInstall.InstalledVersion(root.FullName));
    }

    private Dictionary<string, string> Home() => new() { [EnvironmentVariables.Home] = home.FullName };

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(home);
    }
}
