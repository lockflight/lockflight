using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// Linux: the engine makes itself a child subreaper, so a process its children start and leave behind (a double fork, a
/// <c>setsid</c> or <c>nohup</c> daemon) is re-parented to the engine instead of to PID 1, and a walk of <c>/proc</c> from the engine
/// finds it. A process started outside the engine's tree (<c>sudo systemd-run</c>, a Docker container) is not found; one owned by
/// another user (started through <c>sudo</c>) is found but cannot be stopped.
/// </summary>
internal sealed class LinuxProcessScope(int engine, string? engineUid) : IProcessScope
{
    private const string Proc = "/proc";
    private const int MaxRounds = 5;
    private static readonly TimeSpan StopTime = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    public SweepMechanism Mechanism => SweepMechanism.Subreaper;

    public string? Note => null;

    /// <summary>Makes this process a child subreaper for the rest of its life; a scope that sweeps nothing, with the reason, when that fails.</summary>
    public static IProcessScope Begin()
    {
        try
        {
            if (LinuxNative.Prctl(LinuxNative.SetChildSubreaper, 1, 0, 0, 0) != 0)
            {
                return new NoProcessScope($"the engine could not become a child subreaper (errno {Marshal.GetLastPInvokeError()})");
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return new NoProcessScope($"the C library could not be loaded: {exception.Message}");
        }

        return new LinuxProcessScope(Environment.ProcessId, Uid(Environment.ProcessId));
    }

    public IReadOnlyList<SweptProcess> Sweep()
    {
        Stopwatch clock = Stopwatch.StartNew();
        Dictionary<int, Found> found = [];

        // Each one is stopped where it is, so it cannot start another while the walk goes on, until a walk finds nothing new.
        for (int round = 0; round < MaxRounds && clock.Elapsed < StopTime; round++)
        {
            ProcStat[] fresh = [.. ProcStat.Descendants(Table(), engine).Where(process => !found.ContainsKey(process.Pid))];
            if (fresh.Length == 0)
            {
                break;
            }

            foreach (ProcStat process in fresh)
            {
                found[process.Pid] = Describe(process);
                if (!process.IsZombie)
                {
                    LinuxNative.Kill(process.Pid, LinuxNative.SignalStop);
                }
            }
        }

        if (found.Count == 0)
        {
            return [];
        }

        foreach (Found process in found.Values.Where(process => !process.Stat.IsZombie && IsSame(process.Stat)))
        {
            LinuxNative.Kill(process.Stat.Pid, LinuxNative.SignalKill);
        }

        HashSet<int> alive = [.. found.Keys];
        Stopwatch waiting = Stopwatch.StartNew();
        while (true)
        {
            // A dead one is a zombie until the engine reaps it; one whose parent was a survivor is re-parented to the engine first.
            foreach (int pid in alive.ToArray())
            {
                LinuxNative.WaitPid(pid, out _, LinuxNative.NoHang);
                if (Read(pid) is not ProcStat now || now.StartTime != found[pid].Stat.StartTime || now.IsZombie)
                {
                    alive.Remove(pid);
                }
            }

            if (alive.Count == 0 || waiting.Elapsed >= StopTime)
            {
                break;
            }

            Thread.Sleep(PollInterval);
        }

        return [.. found.Values.Select(process => new SweptProcess(
            process.Stat.Pid,
            process.Image,
            process.Command,
            process.User,
            alive.Contains(process.Stat.Pid) ? SurvivorOutcome.NotStopped : process.Stat.IsZombie ? SurvivorOutcome.Exited : SurvivorOutcome.Stopped))];
    }

    private static List<ProcStat> Table()
    {
        List<ProcStat> table = [];
        foreach (string directory in Directory.EnumerateDirectories(Proc))
        {
            if (int.TryParse(Path.GetFileName(directory), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && Read(pid) is ProcStat stat)
            {
                table.Add(stat);
            }
        }

        return table;
    }

    private static ProcStat? Read(int pid) => ReadText(Path.Combine(Proc, pid.ToString(CultureInfo.InvariantCulture), "stat")) is string line ? ProcStat.Parse(line) : null;

    private static bool IsSame(ProcStat process) => Read(process.Pid) is ProcStat now && now.StartTime == process.StartTime;

    private static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The process has exited, or belongs to a user whose details this one cannot read.
            return null;
        }
    }

    private static string? Uid(int pid) =>
        ReadText(Path.Combine(Proc, pid.ToString(CultureInfo.InvariantCulture), "status"))?
            .Split('\n')
            .FirstOrDefault(line => line.StartsWith("Uid:", StringComparison.Ordinal))?
            .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .ElementAtOrDefault(1);

    private Found Describe(ProcStat process)
    {
        string directory = Path.Combine(Proc, process.Pid.ToString(CultureInfo.InvariantCulture));
        string image = Target(Path.Combine(directory, "exe")) ?? ReadText(Path.Combine(directory, "comm"))?.Trim() ?? "?";
        string command = ReadText(Path.Combine(directory, "cmdline"))?.Replace('\0', ' ').Trim() is { Length: > 0 } line ? line : image;
        string? uid = Uid(process.Pid);
        string? user = uid is null ? null : uid == engineUid ? Environment.UserName : $"uid {uid}";
        return new Found(process, image, command, user);
    }

    private static string? Target(string link)
    {
        try
        {
            return new FileInfo(link).LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Found(ProcStat Stat, string Image, string Command, string? User);
}
