using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// macOS, best effort: there is no subreaper or job object, and a process whose parent exits is re-parented to <c>launchd</c>,
/// losing its link to the engine. Two other links survive it. In CI (a CI job's run only: one a person or an agent starts keeps
/// its caller's process group, so it still gets the caller's interrupt and its time limit) the engine makes itself the leader of a
/// new process group, which every process it starts joins, and so do theirs unless they leave it (<c>setsid</c>, <c>setpgid</c>);
/// and every process the engine starts gets a marker in its environment (<see cref="ProcessSweep.MarkerVariable"/>, a value new for
/// each run), which the processes it starts inherit. A sweep stops every process of the engine's user in that group (but those in it
/// before the engine began, such as the other commands of a pipeline it runs in), every one whose environment carries the marker,
/// and the processes those started while they are still their children. macOS shows a process's environment only for programs
/// that are not part of the system, so a system program (<c>sh</c>, <c>sleep</c>) that left the group is not found, nor is one that
/// left the group and cleared its environment, nor one of another user (<c>sudo</c> resets the environment).
/// </summary>
internal sealed unsafe class MacProcessScope(int engine, uint engineUid, int group, IReadOnlySet<(int Pid, ulong Start)> before, string marker) : IProcessScope
{
    private const int MaxRounds = 5;
    private const int FirstCapacity = 4096;
    private const int MarkerBytes = 16;
    private const int MicrosecondsPerSecond = 1_000_000;
    private const int NameLength = 32;
    private static readonly TimeSpan StopTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    private static readonly Lazy<int> ArgumentsMaximum = new(ReadArgumentsMaximum);

    /// <summary>The processes an earlier sweep could not stop (pid and start time), reported once rather than at every sweep.</summary>
    private readonly HashSet<(int Pid, ulong Start)> givenUp = [];

    public SweepMechanism Mechanism => SweepMechanism.Marker;

    public string? Note => null;

    public string? Marker => marker;

    /// <summary>A scope with a new marker; one that sweeps nothing, with the reason, when the engine cannot read process information.</summary>
    public static IProcessScope Begin(bool ci)
    {
        try
        {
            if (Info(Environment.ProcessId) is not ProcBsdInfo self)
            {
                return new NoProcessScope($"the engine could not read its own process information (errno {Marshal.GetLastPInvokeError()})");
            }

            if (Arguments(Environment.ProcessId) is null)
            {
                return new NoProcessScope($"the engine could not read a process's environment (errno {Marshal.GetLastPInvokeError()})");
            }

            if (ci)
            {
                // Fails for a session leader, which leads its own group already.
                MacNative.SetProcessGroup(0, 0);
            }

            int group = MacNative.ProcessGroup();
            HashSet<(int Pid, ulong Start)> before = [.. Pids((buffer, size) => MacNative.ListAllPids(buffer, size))
                .Select(pid => (Pid: pid, Info: Info(pid)))
                .Where(process => process.Info is { } info && info.ProcessGroup == group)
                .Select(process => (process.Pid, Start(process.Info!.Value)))];
            return new MacProcessScope(Environment.ProcessId, self.Uid, group, before, Convert.ToHexString(RandomNumberGenerator.GetBytes(MarkerBytes)));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return new NoProcessScope($"the system library could not be loaded: {exception.Message}");
        }
    }

    public IReadOnlyList<SweptProcess> Sweep()
    {
        Stopwatch clock = Stopwatch.StartNew();
        Dictionary<int, Found> found = [];

        // Each one is stopped where it is, so it cannot start another while the search goes on, until a search finds nothing new.
        for (int round = 0; round < MaxRounds && clock.Elapsed < StopTime; round++)
        {
            Found[] fresh = [.. Candidates().Where(process => !found.ContainsKey(process.Pid) && !givenUp.Contains((process.Pid, process.Start)))];
            if (fresh.Length == 0)
            {
                break;
            }

            foreach (Found process in fresh)
            {
                found[process.Pid] = process;
                if (!process.IsZombie)
                {
                    MacNative.Kill(process.Pid, MacNative.SignalStop);
                }
            }
        }

        if (found.Count == 0)
        {
            return [];
        }

        foreach (Found process in found.Values.Where(process => !process.IsZombie && StartOf(process.Pid) == process.Start))
        {
            MacNative.Kill(process.Pid, MacNative.SignalKill);
        }

        HashSet<int> alive = [.. found.Keys];
        Stopwatch waiting = Stopwatch.StartNew();
        while (true)
        {
            alive.RemoveWhere(pid => IsGone(pid, found[pid].Start));
            if (alive.Count == 0 || waiting.Elapsed >= StopTime)
            {
                break;
            }

            Thread.Sleep(PollInterval);
        }

        givenUp.UnionWith(alive.Select(pid => (pid, found[pid].Start)));
        return [.. found.Values.Select(process => new SweptProcess(
            process.Pid,
            process.Image,
            process.Command,
            process.User,
            alive.Contains(process.Pid) ? SurvivorOutcome.NotStopped : process.IsZombie ? SurvivorOutcome.Exited : SurvivorOutcome.Stopped))];
    }

    /// <summary>
    /// The processes of the engine's user in its process group (but those there before it began) or carrying the marker, and every
    /// process they started that is still their child.
    /// </summary>
    private List<Found> Candidates()
    {
        List<Found> candidates = [];
        HashSet<int> seen = [engine];
        Queue<int> parents = [];
        foreach (int pid in Pids((buffer, size) => MacNative.ListAllPids(buffer, size)))
        {
            if (pid <= 0 || seen.Contains(pid) || Info(pid) is not { } info || info.Uid != engineUid || before.Contains((pid, Start(info))))
            {
                continue;
            }

            ProcessArguments? arguments = Arguments(pid);
            if (info.ProcessGroup == group || arguments?.Environment.Contains($"{ProcessSweep.MarkerVariable}={marker}") == true)
            {
                seen.Add(pid);
                candidates.Add(Describe(pid, info, arguments));
                parents.Enqueue(pid);
            }
        }

        while (parents.TryDequeue(out int parent))
        {
            foreach (int child in Pids((buffer, size) => MacNative.ListChildPids(parent, buffer, size)).Where(child => child > 0 && seen.Add(child)))
            {
                if (Info(child) is { } info)
                {
                    candidates.Add(Describe(child, info, Arguments(child)));
                    parents.Enqueue(child);
                }
            }
        }

        return candidates;
    }

    private Found Describe(int pid, ProcBsdInfo info, ProcessArguments? arguments)
    {
        string name = NameOf(info);
        string image = arguments?.Image is { Length: > 0 } path ? path : name;
        string command = arguments is { Arguments.Count: > 0 } ? string.Join(' ', arguments.Arguments) : image;
        string user = info.Uid == engineUid ? Environment.UserName : $"uid {info.Uid}";
        return new Found(pid, Start(info), image, command, user, info.Status == MacNative.Zombie);
    }

    private static bool IsGone(int pid, ulong start)
    {
        if (MacNative.Kill(pid, 0) != 0 && Marshal.GetLastPInvokeError() == MacNative.NoSuchProcess)
        {
            return true;
        }

        // A dead process waiting for its parent to reap it is gone; so is a later one that reuses the pid.
        return Info(pid) is not { } info || Start(info) != start || info.Status == MacNative.Zombie;
    }

    private static ulong? StartOf(int pid) => Info(pid) is { } info ? Start(info) : null;

    private static ulong Start(ProcBsdInfo info) => (info.StartSeconds * MicrosecondsPerSecond) + info.StartMicroseconds;

    private static ProcBsdInfo? Info(int pid)
    {
        ProcBsdInfo info = default;
        int size = Unsafe.SizeOf<ProcBsdInfo>();
        return MacNative.ProcessInfo(pid, MacNative.BsdInfo, 0, &info, size) == size ? info : null;
    }

    private static string NameOf(ProcBsdInfo info)
    {
        ReadOnlySpan<byte> name = new(info.Name, NameLength);
        int end = name.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? name : name[..end]);
    }

    /// <summary>The ids a <c>proc_list*pids</c> call gives, growing the buffer until they fit.</summary>
    private static int[] Pids(ListPids list)
    {
        for (int capacity = FirstCapacity; ; capacity *= 4)
        {
            int[] buffer = new int[capacity];
            int count;
            fixed (int* pids = buffer)
            {
                count = list(pids, capacity * sizeof(int));
            }

            if (count < 0)
            {
                return [];
            }

            if (count < capacity)
            {
                return buffer[..count];
            }
        }
    }

    /// <summary>
    /// A process's executable, arguments and environment (<c>KERN_PROCARGS2</c>: the argument count, the executable path, padding,
    /// the arguments, then the environment); null for a process this user cannot read (another user's) or one that has exited.
    /// </summary>
    private static ProcessArguments? Arguments(int pid)
    {
        int maximum = ArgumentsMaximum.Value;
        if (maximum <= sizeof(int))
        {
            return null;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(maximum);
        try
        {
            nuint length = (nuint)maximum;
            int[] argumentsName = [MacNative.KernelControl, MacNative.ProcessArguments, pid];
            fixed (int* name = argumentsName)
            fixed (byte* value = buffer)
            {
                if (MacNative.Sysctl(name, 3, value, ref length, 0, 0) != 0)
                {
                    return null;
                }
            }

            return ProcessArguments.Parse(buffer.AsSpan(0, (int)length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The most bytes a process's arguments and environment take (<c>KERN_ARGMAX</c>); 0 when it cannot be read.</summary>
    private static int ReadArgumentsMaximum()
    {
        int[] maximumName = [MacNative.KernelControl, MacNative.ArgumentsMax];
        int maximum = 0;
        nuint maximumLength = sizeof(int);
        fixed (int* name = maximumName)
        {
            return MacNative.Sysctl(name, 2, (byte*)&maximum, ref maximumLength, 0, 0) == 0 ? maximum : 0;
        }
    }

    private delegate int ListPids(int* buffer, int size);

    private sealed record Found(int Pid, ulong Start, string Image, string Command, string User, bool IsZombie);
}
