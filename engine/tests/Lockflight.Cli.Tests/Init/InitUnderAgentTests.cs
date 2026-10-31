using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Tests.Init;

/// <summary>
/// <c>init</c> run again by the agent (CLAUDECODE=1) on a project that has a manifest: it may add what doctor asks for, but it
/// leaves the engine (the judge) as it is: the pin, the engine lock and the linked engine.
/// </summary>
public sealed class InitUnderAgentTests : IDisposable
{
    private const string OlderPin = """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "0.0.0-alpha.1", "commands": ["lockflight"] } } }""";

    private static readonly Dictionary<string, string> Agent = new() { [EnvironmentVariables.ClaudeCode] = "1" };

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-init-agent-");

    public InitUnderAgentTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 2\n");
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"), OlderPin);
    }

    [Fact]
    public async Task TheAgentsInitKeepsThePinWritesNoLockAndLinksNothing()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init"], "", projectRoot.FullName, Agent);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal(OlderPin, File.ReadAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json")));
        Assert.False(File.Exists(EngineLockFiles.CommittedPath(projectRoot.FullName)));
        Assert.False(File.Exists(LinkCommand.BinaryPath(projectRoot.FullName)));
        Assert.Contains("leaves the engine as it is", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentsDryRunSaysTheEngineIsKept()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["init", "--dry-run"], "", projectRoot.FullName, Agent);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("leaves the engine as it is", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgentsInitOnADevelopmentPinWritesTheManualCiTrigger()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json"), OlderPin.Replace("0.0.0-alpha.1", "0.0.0-dev.20261031", StringComparison.Ordinal));

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--ci-platform", "gitlab"], "", projectRoot.FullName, Agent);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("pins a development build of the engine", File.ReadAllText(Path.Combine(projectRoot.FullName, ".gitlab", "ci", "lockflight-gates.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APersonsInitStillMovesThePin()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["init", "--no-link"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.NotEqual(OlderPin, File.ReadAllText(Path.Combine(projectRoot.FullName, "dotnet-tools.json")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(projectRoot);
}
