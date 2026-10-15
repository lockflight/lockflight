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
    public async Task AMissingExecutableIsReportedAsNotOnPath()
    {
        ExecutableNotFoundException exception = await Assert.ThrowsAsync<ExecutableNotFoundException>(() =>
            ProcessRunner.RunAsync("lockflight-no-such-tool", [], directory.FullName, TestContext.Current.CancellationToken));

        Assert.Contains("'lockflight-no-such-tool' was not found on PATH", exception.Message, StringComparison.Ordinal);
    }

    public void Dispose() => directory.Delete(recursive: true);
}
