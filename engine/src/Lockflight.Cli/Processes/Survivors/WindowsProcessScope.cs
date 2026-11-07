using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    private const int MaxImageLength = 1024;
    private const uint KilledExitCode = 1;
    private const uint EndAccess = WindowsNative.ProcessTerminate | QueryAccess;
    private const uint QueryAccess = WindowsNative.ProcessQueryLimitedInformation | WindowsNative.Synchronize;
    private const string UnknownImage = "?";
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
        Dictionary<int, Ending> found = [];
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
                    found[pid] = Stop(job, pid);
                }
            }

            Stopwatch waiting = Stopwatch.StartNew();
            return [.. found.Select(entry => new SweptProcess(entry.Key, entry.Value.Image, entry.Value.Image, User: null, Outcome(entry.Value, waiting)))];
        }
        finally
        {
            foreach (Ending ending in found.Values)
            {
                ending.Handle?.Dispose();
            }
        }
    }

    /// <summary>
    /// Ends the process <paramref name="pid"/> names if it is still in <paramref name="job"/>. The id was read from the job's list, and
    /// the process may have exited and its id been given to another process since, so the process is opened first, checked to be in
    /// the job through that handle, and ended through the same handle: a process outside the job is never ended.
    /// </summary>
    /// <returns>The opened process (null when it is gone or is not the job's any more), its image, and whether it could not be checked.</returns>
    internal static Ending Stop(nint job, int pid)
    {
        bool canEnd = true;
        SafeProcessHandle handle = WindowsNative.OpenProcess(EndAccess, inherit: false, pid);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error != WindowsNative.AccessDenied)
            {
                // No process has that id any more.
                return new Ending(null, UnknownImage, Unchecked: false);
            }

            canEnd = false;
            handle = WindowsNative.OpenProcess(QueryAccess, inherit: false, pid);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return new Ending(null, UnknownImage, Unchecked: true);
            }
        }

        if (!WindowsNative.IsProcessInJob(handle, job, out bool inJob))
        {
            handle.Dispose();
            return new Ending(null, UnknownImage, Unchecked: true);
        }

        if (!inJob)
        {
            // The job's process exited and its id now names a process outside the job.
            handle.Dispose();
            return new Ending(null, UnknownImage, Unchecked: false);
        }

        string image = Image(handle);
        if (canEnd)
        {
            // It may refuse (it is exiting already, or is protected): waiting on the handle tells which.
            WindowsNative.TerminateProcess(handle, KilledExitCode);
        }

        return new Ending(handle, image, Unchecked: false);
    }

    private static SurvivorOutcome Outcome(Ending ending, Stopwatch waiting)
    {
        if (ending.Handle is not SafeProcessHandle handle)
        {
            return ending.Unchecked ? SurvivorOutcome.NotStopped : SurvivorOutcome.Exited;
        }

        TimeSpan left = StopTime - waiting.Elapsed;
        uint milliseconds = left > TimeSpan.Zero ? (uint)left.TotalMilliseconds : 0;
        return WindowsNative.WaitForSingleObject(handle, milliseconds) == WindowsNative.WaitObject0 ? SurvivorOutcome.Stopped : SurvivorOutcome.NotStopped;
    }

    private static string Image(SafeProcessHandle handle)
    {
        char* name = stackalloc char[MaxImageLength];
        int length = MaxImageLength;
        return WindowsNative.QueryFullProcessImageName(handle, 0, name, ref length) ? new string(name, 0, length) : UnknownImage;
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

    /// <summary>A job process the sweep tried to end: its handle (null when gone or no longer the job's), image, and whether it could not be checked.</summary>
    internal sealed record Ending(SafeProcessHandle? Handle, string Image, bool Unchecked);
}
