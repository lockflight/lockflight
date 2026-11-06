using System.Diagnostics;
using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-process-");

    [Fact]
    public async Task AProcessPastItsTimeLimitIsKilledAndReportedAsTimedOut()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sleep command.");

        ProcessResult result = await ProcessRunner.RunAsync("sleep", ["30"], Path.GetTempPath(), TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.Equal(ProcessRunner.TimedOutExitCode, result.ExitCode);
        Assert.True(result.DurationMs < 10_000);
    }

    [Fact]
    public async Task StandardOutputAndStandardErrorStaySeparate()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sh command.");

        ProcessResult result = await ProcessRunner.RunAsync("sh", ["-c", "echo data; echo noise >&2"], directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal("data", result.Output.Trim());
        Assert.Equal("noise", result.Error.Trim());
        Assert.Contains("data", result.Combined, StringComparison.Ordinal);
        Assert.Contains("noise", result.Combined, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChildProcessCannotReachTheCiRunnersFileCommands()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sh command.");
        string? saved = Environment.GetEnvironmentVariable("GITHUB_STATE");
        Environment.SetEnvironmentVariable("GITHUB_STATE", Path.Combine(directory.FullName, "state"));
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync("sh", ["-c", "echo \"${GITHUB_STATE:-unset} ${GITHUB_ENV:-unset} ${GITHUB_OUTPUT:-unset}\""], directory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal("unset unset unset", result.Output.Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_STATE", saved);
        }
    }

    [Fact]
    public async Task ALargeInputIsWrittenWhileTheOutputIsDrained()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX cat command.");
        string input = Path.Combine(directory.FullName, "input.txt");
        await File.WriteAllTextAsync(input, new string('x', 4 * 1024 * 1024), TestContext.Current.CancellationToken);

        // cat echoes as it reads: without draining its output while writing its input, both pipes fill and neither side moves.
        ProcessResult result = await ProcessRunner.RunAsync("cat", [], directory.FullName, TimeSpan.FromSeconds(60), input, TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal(4 * 1024 * 1024, result.Output.Length);
    }

    [Fact]
    public async Task ToolsRunWithSnapshotDiffToolsDisabled()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sh command.");

        ProcessResult result = await ProcessRunner.RunAsync("sh", ["-c", "echo \"$DiffEngine_Disabled\""], directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal("true", result.Output.Trim());
    }

    [Fact]
    public async Task ToolsRunWithEveryBuildServerTurnedOff()
    {
        ProcessResult result = OperatingSystem.IsWindows()
            ? await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", "echo %UseSharedCompilation% %UseRazorBuildServer% %DOTNET_CLI_USE_MSBUILD_SERVER% %MSBUILDDISABLENODEREUSE%"], directory.FullName, TestContext.Current.CancellationToken)
            : await ProcessRunner.RunAsync("sh", ["-c", "echo \"$UseSharedCompilation $UseRazorBuildServer $DOTNET_CLI_USE_MSBUILD_SERVER $MSBUILDDISABLENODEREUSE\""], directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal("false false 0 1", result.Output.Trim());
    }

    [Fact]
    public async Task MSBuildStartedByTheEngineSeesTheCompilerAndRazorServersTurnedOff()
    {
        File.WriteAllText(Path.Combine(directory.FullName, "empty.proj"), "<Project />\n");

        ProcessResult result = await ProcessRunner.RunAsync(
            "dotnet", ["msbuild", "empty.proj", "-getProperty:UseSharedCompilation", "-getProperty:UseRazorBuildServer"], directory.FullName, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"UseSharedCompilation\": \"false\"", result.Output, StringComparison.Ordinal);
        Assert.Contains("\"UseRazorBuildServer\": \"false\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProcessThatLeavesADaemonHoldingItsOutputReturnsWhenItExits()
    {
        // The daemon inherits the output pipe and keeps it open for a minute: waiting for the pipe to end would stall the run that long.
        ProcessResult result = OperatingSystem.IsWindows()
            ? await ProcessRunner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", "Write-Output before; $daemon = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep 60' -NoNewWindow -PassThru; Write-Output $daemon.Id"],
                directory.FullName,
                TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)
            : await ProcessRunner.RunAsync("sh", ["-c", "echo before; sleep 60 & echo $!"], directory.FullName, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            Assert.Equal(0, result.ExitCode);
            Assert.True(result.OutputHeld);
            Assert.False(result.TimedOut);
            Assert.Equal("before", lines[0]);
            Assert.Contains(OperatingSystem.IsWindows() ? "powershell.exe -NoProfile" : "sh -c", HeldOutputs.Snapshot());
        }
        finally
        {
            StopDaemon(lines);
        }
    }

    [Fact]
    public async Task AProcessWhoseOutputEndsWithItIsNotReportedAsHeld()
    {
        ProcessResult result = OperatingSystem.IsWindows()
            ? await ProcessRunner.RunAsync("cmd.exe", ["/d", "/c", "echo data"], directory.FullName, TestContext.Current.CancellationToken)
            : await ProcessRunner.RunAsync("sh", ["-c", "echo data"], directory.FullName, TestContext.Current.CancellationToken);

        Assert.False(result.OutputHeld);
        Assert.Equal("data", result.Output.Trim());
    }

    [Fact]
    public async Task ABinaryRunReturnsWhenTheProcessExitsThoughADaemonHoldsItsOutput()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Uses the POSIX sh command.");

        BinaryProcessResult result = await ProcessRunner.RunForBytesAsync(
                "sh", ["-c", "cat; sleep 60 & echo $!"], directory.FullName, "before\n"u8.ToArray(), TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        string[] lines = Encoding.UTF8.GetString(result.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            Assert.True(result.OutputHeld);
            Assert.Equal("before", lines[0]);
        }
        finally
        {
            StopDaemon(lines);
        }
    }

    [Fact]
    public async Task AMissingExecutableIsReportedAsNotOnPath()
    {
        ExecutableNotFoundException exception = await Assert.ThrowsAsync<ExecutableNotFoundException>(() =>
            ProcessRunner.RunAsync("lockflight-no-such-tool", [], directory.FullName, TestContext.Current.CancellationToken));

        Assert.Contains("'lockflight-no-such-tool' was not found on PATH", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => directory.Delete(recursive: true);

    private static void StopDaemon(string[] lines)
    {
        if (lines.Length < 2 || !int.TryParse(lines[^1], out int pid))
        {
            return;
        }

        try
        {
            using Process daemon = Process.GetProcessById(pid);
            daemon.Kill();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // Already gone.
        }
    }
}
