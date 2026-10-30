using System.Text.Json;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n")]
    public async Task AnEngineWithoutARecordIsNotRunAndTheUserIsToldToUpdate(string? record)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        LinkEngine(AllowingEngine);
        if (record is not null)
        {
            File.WriteAllText(EngineRecord.PathOf(project.FullName), record);
        }

        (int exitCode, string error) = await RunShimAsync(Write);

        Assert.Equal(2, exitCode);
        Assert.Contains("no hash record", error, StringComparison.Ordinal);
        Assert.Contains(
            "Ask the user to open a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused) and run `~/.lockflight/bin/lockflight update` in the project",
            error,
            StringComparison.Ordinal);
        Assert.Equal(0, (await RunShimAsync(Read)).ExitCode);
    }

    [Fact]
    public async Task AnEngineWithoutARecordDoesNotJudgeATypedApproval()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        LinkEngine(AllowingEngine);

        (int exitCode, string error) = await RunShimAsync(
            """{"hook_event_name":"UserPromptExpansion","expansion_type":"slash_command","command_name":"lockflight:approve","command_args":"spec"}""",
            "user-prompt-expansion");

        Assert.Equal(2, exitCode);
        Assert.Contains("Nothing was approved", error, StringComparison.Ordinal);
        Assert.Contains("lockflight update", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARecordDeletedAfterAPassedCheckIsNotRun(bool gitCheckout)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        if (gitCheckout)
        {
            await ProcessRunner.RunAsync("git", ["init", "-q"], project.FullName, TestContext.Current.CancellationToken);
        }

        RecordEngine(LinkEngine(AllowingEngine));
        Assert.Equal(0, (await RunShimAsync(Write)).ExitCode);

        File.Delete(EngineRecord.PathOf(project.FullName));

        Assert.Equal(2, (await RunShimAsync(Write)).ExitCode);
    }

    [Fact]
    public async Task AtSessionStartThePersonIsToldToUpdateAnEngineWithoutARecord()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        LinkEngine(AllowingEngine);

        (int exitCode, string output) = await RunShimOutputAsync("""{"hook_event_name":"SessionStart"}""", "session-start");

        Assert.Equal(0, exitCode);
        using JsonDocument notice = JsonDocument.Parse(output);
        string message = notice.RootElement.GetProperty("systemMessage").GetString()!;
        Assert.Contains("no hash record", message, StringComparison.Ordinal);
        Assert.Contains(
            "open a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused) and run `~/.lockflight/bin/lockflight update` in the project",
            message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEngineTheLockPinsRuns()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        string hash = EngineRecord.HashOf(LinkEngine(AllowingEngine));
        EngineRecord.Write(project.FullName, hash, "linux-x64");
        WriteLock(".lockflight/engine.lock", "linux-x64", hash);

        (int exitCode, string error) = await RunShimAsync(Write);

        Assert.True(exitCode == 0, error);
    }

    [Theory]
    [InlineData(".lockflight/engine.lock")]
    [InlineData(".lockflight/local/engine.lock")]
    public async Task AnEngineTheLockDoesNotPinIsNotRunAndTheLinkInstalledRecoveryIsAllowed(string lockPath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        string hash = EngineRecord.HashOf(LinkEngine(AllowingEngine));
        EngineRecord.Write(project.FullName, hash, "linux-x64");
        WriteLock(lockPath, "linux-x64", new string('a', 64));

        (int exitCode, string error) = await RunShimAsync(Write);
        (int recovery, _) = await RunShimAsync("""{"tool_name":"Bash","tool_input":{"command":"~/.lockflight/bin/lockflight link --installed"}}""");

        Assert.Equal(2, exitCode);
        Assert.Contains($"it is not the engine {lockPath} pins for this platform", error, StringComparison.Ordinal);
        Assert.Contains("lockflight engine install", error, StringComparison.Ordinal);
        Assert.Equal(0, recovery);
        Assert.Equal(0, (await RunShimAsync(Read)).ExitCode);
    }

    [Fact]
    public async Task AnEngineRecordedWithoutAPlatformIsNotTheLockedOne()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        string hash = EngineRecord.HashOf(LinkEngine(AllowingEngine));
        RecordEngine(LinkCommand.BinaryPath(project.FullName));
        WriteLock(".lockflight/engine.lock", "linux-x64", hash);

        Assert.Equal(2, (await RunShimAsync(Write)).ExitCode);
    }

    [Fact]
    public async Task ALockChangedAfterAPassedCheckIsCheckedAgain()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in engine is a POSIX sh script.");
        await ProcessRunner.RunAsync("git", ["init", "-q"], project.FullName, TestContext.Current.CancellationToken);
        string hash = EngineRecord.HashOf(LinkEngine(AllowingEngine));
        EngineRecord.Write(project.FullName, hash, "linux-x64");
        WriteLock(".lockflight/engine.lock", "linux-x64", hash);
        Assert.Equal(0, (await RunShimAsync(Write)).ExitCode);

        WriteLock(".lockflight/engine.lock", "linux-x64", new string('b', 64));
        File.SetLastWriteTimeUtc(Path.Combine(project.FullName, ".lockflight", "engine.lock"), DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(2, (await RunShimAsync(Write)).ExitCode);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(project);

    private void WriteLock(string relativePath, string rid, string hash)
    {
        string path = Path.Combine(project.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $$"""
            {
              "schema": "lockflight.engine-lock/v1",
              "package": "Lockflight.Cli",
              "version": "0.3.0",
              "channel": "release",
              "installScript": { "sha256": "{{new string('5', 64)}}" },
              "binaries": {
                "{{rid}}": { "sha256": "{{hash}}" }
              }
            }

            """);
    }

    private string LinkEngine(string script)
    {
        string binary = LinkCommand.BinaryPath(project.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);

        // A passed check leaves the engine read-only, so a new one is renamed into place, as link does.
        string temporary = binary + ".new";
        File.WriteAllText(temporary, script);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, LinkCommand.ExecutableMode);
        }

        File.Move(temporary, binary, overwrite: true);
        return binary;
    }

    private void RecordEngine(string binary) => EngineRecord.Write(project.FullName, EngineRecord.HashOf(binary), rid: null);

    private async Task<(int ExitCode, string Error)> RunShimAsync(string payload, string hookEvent = "pre-tool-use")
    {
        ProcessResult run = await RunShimProcessAsync(payload, hookEvent);
        return (run.ExitCode, run.Error);
    }

    private async Task<(int ExitCode, string Output)> RunShimOutputAsync(string payload, string hookEvent)
    {
        ProcessResult run = await RunShimProcessAsync(payload, hookEvent);
        return (run.ExitCode, run.Output);
    }

    private async Task<ProcessResult> RunShimProcessAsync(string payload, string hookEvent)
    {
        string input = Path.Combine(project.FullName, "payload.json");
        File.WriteAllText(input, payload);
        return await ProcessRunner.RunAsync(
            "env", [$"CLAUDE_PROJECT_DIR={project.FullName}", "sh", ShimLocation.Path, hookEvent], project.FullName, TimeSpan.FromSeconds(30), input, TestContext.Current.CancellationToken);
    }
}
