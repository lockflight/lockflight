using System.Runtime.CompilerServices;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Tests.Processes.Survivors;

[Collection(SerialProcessScope.Name)]
public sealed class ProcessScopeTests
{
    private const string DaemonSeconds = "77";

    [Fact]
    public async Task ADaemonAChildLeftRunningIsFoundAndStopped()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsWindows(), "Processes left running are looked for on Linux and Windows.");
        IProcessScope scope = ProcessScopes.Begin();
        Assert.True(scope.Mechanism != SweepMechanism.None, scope.Note);
        scope.Sweep();

        // The daemon leaves its parent's session (Linux) or is started through the shell (Windows): killing the child's tree would miss it.
        ProcessResult start = OperatingSystem.IsWindows()
            ? await ProcessRunner.RunAsync(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command", $"Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-Command','Start-Sleep {DaemonSeconds}' -WindowStyle Hidden"],
                Path.GetTempPath(),
                TestContext.Current.CancellationToken)
            : await ProcessRunner.RunAsync("sh", ["-c", $"setsid sleep {DaemonSeconds} >/dev/null 2>&1 </dev/null &"], Path.GetTempPath(), TestContext.Current.CancellationToken);
        Assert.Equal(0, start.ExitCode);

        SweptProcess daemon = Assert.Single(scope.Sweep(), IsDaemon);
        Assert.Equal(SurvivorOutcome.Stopped, daemon.Outcome);
        Assert.DoesNotContain(scope.Sweep(), IsDaemon);
    }

    [Fact]
    public async Task TheRunnerCountsItsChildrenForTheCurrentSweep()
    {
        ProcessSweepTests.FakeScope scope = new();
        using ProcessSweep sweep = ProcessSweep.Start(scope);

        await ProcessRunner.RunAsync("git", ["--version"], Path.GetTempPath(), TestContext.Current.CancellationToken);

        Assert.Equal(1, scope.Sweeps);
    }

    [Fact]
    public void TheJobLimitsHaveWindowsLayout()
    {
        Assert.SkipUnless(Environment.Is64BitProcess, "The layout is checked for 64-bit processes.");

        Assert.Equal(144, Unsafe.SizeOf<JobLimits>());
    }

    private static bool IsDaemon(SweptProcess process) => OperatingSystem.IsWindows()
        ? process.Image.Contains("powershell", StringComparison.OrdinalIgnoreCase)
        : process.Command.Contains($"sleep {DaemonSeconds}", StringComparison.Ordinal);
}
