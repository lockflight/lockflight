using Lockflight.Cli.Config;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShimEngineRecordTests : IDisposable
{
    private const string AllowingEngine = "#!/bin/sh\ncat > /dev/null\nexit 0\n";
    private const string Write = """{"tool_name":"Write","tool_input":{"file_path":"src/a.cs","content":"x"}}""";
    private const string Read = """{"tool_name":"Read","tool_input":{"file_path":"src/a.cs"}}""";

    private readonly DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-shim-record-");

    public ShimEngineRecordTests() => File.WriteAllText(Path.Combine(project.FullName, Manifest.FileName), "version: 1\n");

    [Fact]
    public async Task AnEngineThatMatchesTheHashLinkRecordedRuns()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        RecordEngine(LinkEngine(AllowingEngine));

        (int exitCode, string error) = await RunShimAsync(Write);

        Assert.True(exitCode == 0, error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnEngineReplacedAfterLinkingIsNotRun(bool gitCheckout)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        if (gitCheckout)
        {
            await ProcessRunner.RunAsync("git", ["init", "-q"], project.FullName, TestContext.Current.CancellationToken);
        }

        RecordEngine(LinkEngine(AllowingEngine));
        Assert.Equal(0, (await RunShimAsync(Write)).ExitCode);

        string binary = LinkEngine(AllowingEngine + "# another program\n");
        File.SetLastWriteTimeUtc(binary, DateTime.UtcNow.AddMinutes(1));
        (int exitCode, string error) = await RunShimAsync(Write);

        Assert.Equal(2, exitCode);
        Assert.Contains("SHA-256", error, StringComparison.Ordinal);
        Assert.Contains("dotnet tool restore && dotnet lockflight link", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordChangedAfterAPassedCheckIsCheckedAgain()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        RecordEngine(LinkEngine(AllowingEngine));
        Assert.Equal(0, (await RunShimAsync(Write)).ExitCode);

        File.WriteAllText(EngineRecord.PathOf(project.FullName), new string('0', 64) + "\n");
        File.SetLastWriteTimeUtc(EngineRecord.PathOf(project.FullName), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(2, (await RunShimAsync(Write)).ExitCode);
    }

    [Fact]
    public async Task AReplacedEngineStillAllowsReading()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        LinkEngine(AllowingEngine);
        File.WriteAllText(EngineRecord.PathOf(project.FullName), new string('0', 64) + "\n");

        Assert.Equal(0, (await RunShimAsync(Read)).ExitCode);
        Assert.Equal(2, (await RunShimAsync(Write)).ExitCode);
    }

    [Fact]
    public async Task AnEngineLinkedWithoutARecordStillRuns()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        LinkEngine(AllowingEngine);

        (int exitCode, string error) = await RunShimAsync(Write);

        Assert.True(exitCode == 0, error);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(project);

    private string LinkEngine(string script)
    {
        string binary = LinkCommand.BinaryPath(project.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(binary, LinkCommand.ExecutableMode);
        }

        return binary;
    }

    private void RecordEngine(string binary) => EngineRecord.Write(project.FullName, EngineRecord.HashOf(binary));

    private async Task<(int ExitCode, string Error)> RunShimAsync(string payload)
    {
        string input = Path.Combine(project.FullName, "payload.json");
        File.WriteAllText(input, payload);
        ProcessResult run = await ProcessRunner.RunAsync(
            "env", [$"CLAUDE_PROJECT_DIR={project.FullName}", "sh", ShimLocation.Path, "pre-tool-use"], project.FullName, TimeSpan.FromSeconds(30), input, TestContext.Current.CancellationToken);
        return (run.ExitCode, run.Error);
    }
}
