using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Tests.Processes.Survivors;

[Collection(SerialProcessScope.Name)]
public sealed class ProcessScopeTests
{
    private const string DaemonSeconds = "77";
    private const string MacDaemonSeconds = "78";

    [Fact]
    public async Task ADaemonAChildLeftRunningIsFoundAndStopped()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsWindows(), "Processes left running are looked for on Linux and Windows.");
        IProcessScope scope = ProcessScopes.Begin(ci: true);
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
    public void OnMacOSARunOutsideCiKeepsItsCallersProcessGroup()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Only macOS changes the engine's process group.");
        int group = MacNative.ProcessGroup();

        IProcessScope scope = ProcessScopes.Begin(ci: false);

        // An agent's ci --platform local must stay where the agent's interrupt and time limit reach it.
        Assert.Equal(SweepMechanism.Marker, scope.Mechanism);
        Assert.Equal(group, MacNative.ProcessGroup());
    }

    [Fact]
    public async Task OnMacOSADaemonLeftRunningIsFoundAndStopped()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The process group and the marker are how macOS finds processes left running.");
        IProcessScope scope = ProcessScopes.Begin(ci: true);
        Assert.True(scope.Mechanism == SweepMechanism.Marker, scope.Note);
        scope.Sweep();

        // The shell exits at once, so the daemon is re-parented to launchd: its process group links it to the run. Until it has
        // started sleep it is still a copy of the shell, so the command is matched by what it contains.
        ProcessStartInfo start = new("sh", ["-c", $"nohup sleep {MacDaemonSeconds} >/dev/null 2>&1 </dev/null &"]) { UseShellExecute = false };
        start.Environment[ProcessSweep.MarkerVariable] = scope.Marker;
        using (Process shell = Process.Start(start)!)
        {
            await shell.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, shell.ExitCode);
        }

        SweptProcess daemon = Assert.Single(scope.Sweep(), process => process.Command.Contains($"sleep {MacDaemonSeconds}", StringComparison.Ordinal));
        Assert.Equal(SurvivorOutcome.Stopped, daemon.Outcome);
        Assert.DoesNotContain(scope.Sweep(), process => process.Command.Contains($"sleep {MacDaemonSeconds}", StringComparison.Ordinal));
    }

    [Fact]
    public void TheMacProcessInformationHasItsLayout() => Assert.Equal(136, Unsafe.SizeOf<ProcBsdInfo>());

    [Fact]
    public void AProcessesArgumentsAndEnvironmentAreRead()
    {
        byte[] data = [2, 0, 0, 0, .. "/bin/sleep\0\0\0sleep\0600\0LOCKFLIGHT_RUN=AB\0HOME=/x\0\0"u8];

        ProcessArguments arguments = ProcessArguments.Parse(data)!;

        Assert.Equal("/bin/sleep", arguments.Image);
        Assert.Equal(["sleep", "600"], arguments.Arguments);
        Assert.Equal(["LOCKFLIGHT_RUN=AB", "HOME=/x"], arguments.Environment);
        Assert.Null(ProcessArguments.Parse([1, 0]));
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

    [Fact]
    public void AProcessOutsideTheJobIsNeverEnded()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Job objects are a Windows mechanism.");
        nint job = WindowsNative.CreateJobObject(0, 0);
        using Process outsider = Sleeper();
        try
        {
            // As when a job process has exited and its id now names a process the job never held.
            WindowsProcessScope.Ending ending = WindowsProcessScope.Stop(job, outsider.Id);

            Assert.Null(ending.Handle);
            Assert.False(ending.Unchecked);
            Assert.False(outsider.WaitForExit(TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            outsider.Kill();
            WindowsNative.CloseHandle(job);
        }
    }

    [Fact]
    public void AProcessInTheJobIsEndedThroughTheHandleThatCheckedIt()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Job objects are a Windows mechanism.");
        nint job = WindowsNative.CreateJobObject(0, 0);
        using Process member = Sleeper();
        try
        {
            Assert.True(WindowsNative.AssignProcessToJobObject(job, member.Handle));

            WindowsProcessScope.Ending ending = WindowsProcessScope.Stop(job, member.Id);
            using SafeHandle? handle = ending.Handle;

            Assert.NotNull(handle);
            Assert.True(member.WaitForExit(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (!member.HasExited)
            {
                member.Kill();
            }

            WindowsNative.CloseHandle(job);
        }
    }

    private static Process Sleeper() =>
        Process.Start(new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", $"Start-Sleep {DaemonSeconds}"]) { CreateNoWindow = true })!;

    private static bool IsDaemon(SweptProcess process) => OperatingSystem.IsWindows()
        ? process.Image.Contains("powershell", StringComparison.OrdinalIgnoreCase)
        : process.Command.Contains($"sleep {DaemonSeconds}", StringComparison.Ordinal);
}
