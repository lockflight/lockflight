using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
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
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        FakeToolFeed feed = new("0.2.0-alpha.5");

        (ExitCode exitCode, string output, _) = await RunAsync(["update", "--source", "/feeds/local"], feed);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Installed Lockflight 0.2.0-alpha.5", output, StringComparison.Ordinal);
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        (string fileName, IReadOnlyList<string> arguments, string workingDirectory) = feed.Calls[^1];
        Assert.Equal(UserInstall.Engine(home.FullName), fileName);
        Assert.Equal(["update", "--project", "--source", "/feeds/local"], arguments);
        Assert.Equal(root.FullName, workingDirectory);
    }

    [Fact]
    public async Task AFeedThatCannotBeReadFailsWithTheReasonAndLeavesTheProjectAlone()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        FakeToolFeed feed = new("0.2.0-alpha.5") { Fails = true };

        (ExitCode exitCode, _, string error) = await RunAsync(["update"], feed);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.DoesNotContain(feed.Calls, call => call.Arguments.Contains("--project"));
        Assert.Contains("NU1101", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstallFromSourcePullsTheCheckoutAndRunsItsInstallScript()
    {
        string checkout = Directory.CreateTempSubdirectory("lockflight checkout ").FullName;
        Directory.CreateDirectory(UserInstall.Directory(home.FullName));
        File.WriteAllText(Path.Combine(UserInstall.Directory(home.FullName), "source"), checkout);
        FakeToolFeed feed = new("0.2.0-alpha.5");

        (ExitCode exitCode, string output, _) = await RunAsync(["update"], feed);

        Directory.Delete(checkout);
        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Not in a Lockflight project", output, StringComparison.Ordinal);
        Assert.Equal(["git", "-C", checkout, "pull", "--ff-only", "--quiet"], [feed.Calls[0].FileName, .. feed.Calls[0].Arguments]);
        (string script, IReadOnlyList<string> arguments, string workingDirectory) = feed.Calls[1];
        Assert.Equal(checkout, workingDirectory);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("powershell", script);
            Assert.Equal(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(checkout, "scripts", "dev-install.ps1"), "-Updating"], arguments);
        }
        else
        {
            Assert.Equal("sh", script);
            Assert.Equal(Path.Combine(checkout, "scripts", "dev-install.sh"), arguments[^1]);
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
            {"version":1,"isRoot":true,"tools":{"lockflight.cli":{"version":"0.0.0-dev.1","commands":["lockflight"]}}}
            """);
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), $"# Team\n\n{ClaudeMdBlock.Begin}\nold\n{ClaudeMdBlock.End}\n");

        string engine = Path.Combine(home.FullName, "lockflight-native");
        File.WriteAllText(engine, "native engine");

        (ExitCode first, string output, _) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));
        (_, string again, _) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));

        Assert.Equal(ExitCode.Pass, first);
        Assert.Equal(ToolPin.RunningVersion, ToolPin.ReadPinnedVersion(root.FullName));
        Assert.True(ClaudeMdBlock.IsCurrent(File.ReadAllText(Path.Combine(root.FullName, "CLAUDE.md"))));
        Assert.Contains("updated lockflight.yaml, dotnet-tools.json, CLAUDE.md", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root.FullName, ".lockflight", "bin", OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight")));
        Assert.Contains("already up to date", again, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProjectUpdateMigratesAVersionOneManifestAndNamesTheNewChecks()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "# ours\nversion: 1\nprotected: [\"a/**\"]\n");
        string engine = Path.Combine(home.FullName, "lockflight-native");
        File.WriteAllText(engine, "native engine");

        (ExitCode exitCode, string output, _) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));

        Assert.Equal(ExitCode.Pass, exitCode);
        Manifest migrated = ManifestLocator.Discover(root.FullName).Manifest!;
        Assert.Equal((2, ApprovalCheckMode.Report, GateMode.Report), (migrated.Version, migrated.Approvals.Check, migrated.Gates.Suppressions));
        Assert.StartsWith("# ours\nversion: 2\n", File.ReadAllText(Path.Combine(root.FullName, Manifest.FileName)), StringComparison.Ordinal);
        Assert.Contains("lockflight.yaml is now version 2", output, StringComparison.Ordinal);
        Assert.Contains("gates.quality.suppressions: report", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUpdateToAnEngineThatIsNoDevelopmentBuildRemovesTheStaleLocalLock()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 2\n");
        string localLock = EngineLockFiles.LocalPath(root.FullName);
        EngineLockFiles.Write(localLock, new EngineLock(
            "0.2.0-alpha.9.dev.20261030093012", EngineLockChannel.Development, new string('5', 64),
            new Dictionary<string, string> { [EngineRids.Current ?? "linux-x64"] = new string('a', 64) }));
        string engine = Path.Combine(home.FullName, "lockflight-native");
        File.WriteAllText(engine, "native engine");

        (ExitCode exitCode, string output, string error) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.False(File.Exists(localLock));
        Assert.Contains(".lockflight/local/engine.lock", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADryRunShowsTheChangesAndWritesNothing()
    {
        const string before = "version: 1\n";
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), before);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["update", "--project", "--dry-run"], "", root.FullName, Home());

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("nothing was written", output, StringComparison.Ordinal);
        Assert.Contains("+version: 2", output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(root.FullName, Manifest.FileName)));
        Assert.False(File.Exists(Path.Combine(root.FullName, "dotnet-tools.json")));
    }

    [Fact]
    public async Task AnInvalidVersionOneManifestStopsTheUpdateBeforeAnythingIsWritten()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\ngates:\n  nonsense: 1\n");

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["update", "--project"], "", root.FullName, Home());

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("nothing was changed", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.FullName, "dotnet-tools.json")));
    }

    [Fact]
    public async Task AManifestTheMigrationCannotEditGetsTheSnippetAndStaysAsItIs()
    {
        const string before = "version: 1\ngates: { timeoutSeconds: 600 }\n";
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), before);
        string engine = Path.Combine(home.FullName, "lockflight-native");
        File.WriteAllText(engine, "native engine");

        (ExitCode exitCode, _, string error) = await ProjectUpdateAsync(new RunningEngine(engine, IsSingleFile: true));

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Add this by hand", error, StringComparison.Ordinal);
        Assert.Contains("routing:", error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(root.FullName, Manifest.FileName)));
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
