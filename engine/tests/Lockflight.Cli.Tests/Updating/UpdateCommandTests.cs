using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.Updating;

public sealed class UpdateCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-update-");

    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    [Fact]
    public async Task WithoutACheckoutUpdateInstallsFromTheFeedAndHandsTheProjectToTheNewEngine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        FakeToolFeed feed = new("0.2.0-alpha.5");

        (ExitCode exitCode, string output, _) = await RunAsync(["update", "--source", "/feeds/local"], feed);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Installed Lockflight 0.2.0-alpha.5", output, StringComparison.Ordinal);
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        (string fileName, IReadOnlyList<string> arguments, string workingDirectory) = feed.Calls[^1];
        Assert.Equal(UserInstall.Engine(home.FullName), fileName);
        Assert.Equal(["update", "--project"], arguments);
        Assert.Equal(root.FullName, workingDirectory);
    }

    [Fact]
    public async Task AFeedThatCannotBeReadFailsWithTheReasonAndLeavesTheProjectAlone()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        FakeToolFeed feed = new("0.2.0-alpha.5") { Fails = true };

        (ExitCode exitCode, _, string error) = await RunAsync(["update"], feed);

        Assert.Equal(OperatingSystem.IsWindows() ? ExitCode.Config : ExitCode.Error, exitCode);
        Assert.DoesNotContain(feed.Calls, call => call.Arguments.Contains("--project"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("NU1101", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnInstallFromSourceDoesNotTakeAFeed()
    {
        string checkout = Directory.CreateTempSubdirectory("lockflight-checkout-").FullName;
        Directory.CreateDirectory(UserInstall.Directory(home.FullName));
        File.WriteAllText(Path.Combine(UserInstall.Directory(home.FullName), "source"), checkout);
        FakeToolFeed feed = new("0.2.0-alpha.5");

        (ExitCode exitCode, _, string error) = await RunAsync(["update", "--source", "/feeds/local"], feed);

        Directory.Delete(checkout);
        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("delete ~/.lockflight/source", error, StringComparison.Ordinal);
        Assert.Empty(feed.Calls);
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--feed")]
    public async Task AnUnknownOrIncompleteOptionIsRefused(string option)
    {
        FakeToolFeed feed = new("0.2.0-alpha.5");

        (ExitCode exitCode, _, string error) = await RunAsync(["update", option], feed);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--source <feed>", error, StringComparison.Ordinal);
        Assert.Empty(feed.Calls);
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

        string engine = Path.Combine(home.FullName, "lockflight-native");
        File.WriteAllText(engine, "native engine");

        (ExitCode first, string output, _) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));
        (_, string again, _) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));

        Assert.Equal(ExitCode.Pass, first);
        Assert.Equal(ToolPin.RunningVersion, ToolPin.ReadPinnedVersion(root.FullName));
        Assert.True(ClaudeMdBlock.IsCurrent(File.ReadAllText(Path.Combine(root.FullName, "CLAUDE.md"))));
        Assert.Contains("updated dotnet-tools.json, CLAUDE.md", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root.FullName, ".lockflight", "bin", OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight")));
        Assert.Contains("already up to date", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectUpdateNeverLowersANewerPin()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        const string tools = """{"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"99.0.0","commands":["lockflight"]}}}""";
        File.WriteAllText(Path.Combine(root.FullName, "dotnet-tools.json"), tools);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["update", "--project"], "", root.FullName, Home());

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("99.0.0", error, StringComparison.Ordinal);
        Assert.Equal(tools, File.ReadAllText(Path.Combine(root.FullName, "dotnet-tools.json")));
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

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, FakeToolFeed feed)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(Home()),
            Processes = feed.RunAsync,
        };

        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private async Task<(ExitCode ExitCode, string Output, string Error)> ProjectUpdateAsync(RunningEngine engine)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(Home()),
            Engine = engine,
        };

        ExitCode exitCode = await CliApplication.RunAsync(["update", "--project"], context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private Dictionary<string, string> Home() => new() { [EnvironmentVariables.Home] = home.FullName };

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(home);
    }
}
