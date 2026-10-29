using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary><c>doctor --self-test</c>: the few canaries the product runs on a person's machine, in seconds and without a build.</summary>
public sealed class SelfTestTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-self-test-");

    [Fact]
    public async Task DoctorSelfTestCatchesEachPlantedViolationWithoutTouchingTheWorkingDirectory()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["doctor", "--self-test"], string.Empty, root.FullName);

        string[] lines = [.. output.Split('\n').Where(line => line.Length > 0)];
        Assert.Equal(ExitCode.Pass, exitCode);
        string[] names = [GuardCanary.Name, HumanOnlyCanary.Name, LinkedEngineCanary.Name, TestGateCanary.Name, RedCanary.Name];
        Assert.Equal(names.Length + 1, lines.Length);
        Assert.All(names.Zip(lines), pair => Assert.StartsWith($"caught  {pair.First}: ", pair.Second, StringComparison.Ordinal));
        Assert.StartsWith("5 of 5 self-test canaries caught in ", lines[^1], StringComparison.Ordinal);
        Assert.Empty(root.EnumerateFileSystemInfos());
    }

    [Theory]
    [InlineData("--canaries")]
    [InlineData("--canaries=full")]
    public async Task TheDeprecatedCanaryOptionsRunTheSelfTestWithANote(string option)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["doctor", option], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains($"{option} is deprecated; it runs --self-test", error, StringComparison.Ordinal);
        Assert.Contains("5 of 5 self-test canaries caught", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownDoctorOptionIsAUsageError()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["doctor", "--canaries=fast"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--self-test", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGuardThatBlocksHarmlessWorkTooIsBrokenNotCaught()
    {
        using CanaryScratch scratch = new(TimeProvider.System);
        string marker = scratch.PathOf(LockdownPolicy.RelativeMarkerPath);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, "lockdown\n");

        CanaryReport report = await GuardCanary.RunAsync(scratch, TestContext.Current.CancellationToken);

        Assert.Equal(CanaryResult.Broken, report.Result);
        Assert.Contains("blocks a Write in src/ too", report.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnyCanaryNotCaughtFailsTheSelfTestAndIsNamed(bool missed)
    {
        CanaryResult result = missed ? CanaryResult.Missed : CanaryResult.Broken;
        CanaryReport[] reports = [new("guard: one", CanaryResult.Caught, "blocked"), new("guard: two", result, "let through")];
        await using StringWriter output = new();

        ExitCode exitCode = await SelfTestSuite.ReportAsync(reports, TimeSpan.FromMilliseconds(12), output);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains($"{(missed ? "MISSED" : "BROKEN")}  guard: two: let through", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 of 2 self-test canaries caught in 12 ms.", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoCanaryAtAllIsNotAPass() => Assert.Equal(ExitCode.Fail, SelfTestSuite.ExitCodeFor([]));

    public void Dispose() => root.Delete(recursive: true);
}
