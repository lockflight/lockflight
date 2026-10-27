using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Canaries;

public sealed class CanarySuiteTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-canaries-");

    [Fact]
    public async Task DoctorCanariesCatchesEveryPlantedViolationWithoutTouchingTheWorkingDirectory()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["doctor", "--canaries"], string.Empty, root.FullName);

        string[] lines = output.Split('\n');
        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.All(lines.Where(line => line.Contains(": ", StringComparison.Ordinal) && !line.StartsWith("The ", StringComparison.Ordinal)),
            line => Assert.StartsWith("caught", line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("guard: a hard link to lockflight.yaml is blocked", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("approve relay: a CronCreate that sends /lockflight:approve is blocked", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("human-only: 'update --source' refuses under CLAUDECODE=1", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("test gate (VSTest): every test skipped is not a PASS", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("red: a test failing by Assert.Fail is rejected", StringComparison.Ordinal));
        Assert.Contains("canaries caught.", output, StringComparison.Ordinal);
        Assert.Empty(root.EnumerateFileSystemInfos());
    }

    [Fact]
    public async Task AnUnknownCanaryModeIsAUsageError()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["doctor", "--canaries=fast"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--canaries=full", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGuardThatBlocksHarmlessWorkTooIsBrokenNotCaught()
    {
        using CanaryScratch scratch = new(TimeProvider.System);
        string marker = scratch.PathOf(LockdownPolicy.RelativeMarkerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "lockdown\n");

        IReadOnlyList<CanaryReport> reports = await GuardCanaries.RunAsync(scratch, TestContext.Current.CancellationToken);

        CanaryReport report = Assert.Single(reports);
        Assert.Equal(CanaryResult.Broken, report.Result);
        Assert.Contains("blocks a harmless call too", report.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnyCanaryNotCaughtFailsTheSelfTestAndIsNamed(bool missed)
    {
        CanaryResult result = missed ? CanaryResult.Missed : CanaryResult.Broken;
        CanaryReport[] reports = [new("guard: one", CanaryResult.Caught, "blocked"), new("guard: two", result, "let through")];
        await using StringWriter output = new();

        ExitCode exitCode = await CanarySuite.ReportAsync(reports, CanaryMode.Full, output);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains($"{(missed ? "MISSED" : "BROKEN")}  guard: two: let through", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 of 2 canaries caught.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoCanaryAtAllIsNotAPass() => Assert.Equal(ExitCode.Fail, CanarySuite.ExitCodeFor([]));

    public void Dispose() => root.Delete(recursive: true);
}
