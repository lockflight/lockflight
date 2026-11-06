using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// Windows: the engine puts itself in a job object, so every process it or its descendants create joins the job, whatever the
/// parent does (<c>start /b</c>, a detached <c>Process.Start</c>), and the job's process list finds it. The engine keeps a handle
/// that can only list and end the job's processes, so code that copies it cannot let a process leave the job; ending the job ends
/// the engine too, which leaves no evidence. When the engine exits, the job ends whatever is left in it. A process a service
/// starts on the pull request's behalf (WMI, the Task Scheduler) is not in the job and is not found.
/// </summary>
internal sealed unsafe class WindowsProcessScope(nint job, int engine) : IProcessScope
{
    private const int MaxRounds = 5;
    private const int FirstCapacity = 64;
    private const int ListHeaderSize = 8;
    private static readonly TimeSpan StopTime = TimeSpan.FromSeconds(2);

    public SweepMechanism Mechanism => SweepMechanism.Job;

    public string? Note => null;

    /// <summary>Puts this process in a new job for the rest of its life; a scope that sweeps nothing, with the reason, when that fails.</summary>
    public static IProcessScope Begin()
    {
        nint full = WindowsNative.CreateJobObject(0, 0);
        if (full == 0)
        {
            return new NoProcessScope($"the engine could not create a job object (error {Marshal.GetLastPInvokeError()})");
        }

        try
        {
            JobLimits limits = new() { LimitFlags = WindowsNative.KillOnJobClose };
            if (!WindowsNative.SetInformationJobObject(full, WindowsNative.ExtendedLimitInformation, ref limits, Unsafe.SizeOf<JobLimits>()))
            {
                return new NoProcessScope($"the engine could not set up a job object (error {Marshal.GetLastPInvokeError()})");
            }

            // The reduced handle first: once the engine is in the job, closing the last handle to it would end the engine.
            if (!WindowsNative.DuplicateHandle(
                WindowsNative.CurrentProcess, full, WindowsNative.CurrentProcess, out nint reduced, WindowsNative.JobQuery | WindowsNative.JobTerminate, inherit: false, 0))
            {
                return new NoProcessScope($"the engine could not copy its job handle (error {Marshal.GetLastPInvokeError()})");
            }

            if (!WindowsNative.AssignProcessToJobObject(full, WindowsNative.CurrentProcess))
            {
                int error = Marshal.GetLastPInvokeError();
                WindowsNative.CloseHandle(reduced);
                return new NoProcessScope($"the engine could not join a job object (error {error})");
            }

            return new WindowsProcessScope(reduced, Environment.ProcessId);
        }
        finally
        {
            WindowsNative.CloseHandle(full);
        }
    }

    public IReadOnlyList<SweptProcess> Sweep()
    {
        Stopwatch clock = Stopwatch.StartNew();
        Dictionary<int, (Process? Process, string Image, bool Refused)> found = [];
        try
        {
            for (int round = 0; round < MaxRounds && clock.Elapsed < StopTime; round++)
            {
                int[] fresh = [.. Members().Where(pid => pid != engine && !found.ContainsKey(pid))];
                if (fresh.Length == 0)
                {
                    break;
                }

                foreach (int pid in fresh)
                {
                    found[pid] = Kill(pid);
                }
            }

            Stopwatch waiting = Stopwatch.StartNew();
            return [.. found.Select(entry => new SweptProcess(entry.Key, entry.Value.Image, entry.Value.Image, User: null, Outcome(entry.Value, waiting)))];
        }
        finally
        {
            foreach ((Process? process, _, _) in found.Values)
            {
                process?.Dispose();
            }
        }
    }

    private static (Process? Process, string Image, bool Refused) Kill(int pid)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return (null, "?", false);
        }

        string image = Image(process);
        try
        {
            process.Kill();
            return (process, image, false);
        }
        catch (Win32Exception)
        {
            return (process, image, true);
        }
        catch (InvalidOperationException)
        {
            // It exited before the kill.
            return (process, image, false);
        }
    }

    private static SurvivorOutcome Outcome((Process? Process, string Image, bool Refused) found, Stopwatch waiting)
    {
        if (found.Process is not Process process)
        {
            return SurvivorOutcome.Exited;
        }

        TimeSpan left = StopTime - waiting.Elapsed;
        return !found.Refused && process.WaitForExit(left > TimeSpan.Zero ? left : TimeSpan.Zero) ? SurvivorOutcome.Stopped : SurvivorOutcome.NotStopped;
    }

    private static string Image(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? process.ProcessName;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            try
            {
                return process.ProcessName;
            }
            catch (InvalidOperationException)
            {
                return "?";
            }
        }
    }

    /// <summary>The ids of the job's processes, the engine among them (<c>JOBOBJECT_BASIC_PROCESS_ID_LIST</c>: two counts, then the ids).</summary>
    private int[] Members()
    {
        for (int capacity = FirstCapacity; ; capacity *= 4)
        {
            byte[] buffer = new byte[ListHeaderSize + (capacity * IntPtr.Size)];
            fixed (byte* list = buffer)
            {
                if (WindowsNative.QueryInformationJobObject(job, WindowsNative.BasicProcessIdList, list, buffer.Length, out _))
                {
                    int count = BitConverter.ToInt32(buffer, sizeof(uint));
                    return [.. Enumerable.Range(0, count).Select(index => IntPtr.Size == sizeof(long)
                        ? (int)BitConverter.ToInt64(buffer, ListHeaderSize + (index * sizeof(long)))
                        : BitConverter.ToInt32(buffer, ListHeaderSize + (index * sizeof(int))))];
                }
            }

            if (Marshal.GetLastPInvokeError() != WindowsNative.MoreData)
            {
                return [];
            }
        }
    }
}
