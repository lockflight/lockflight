using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The libSystem calls the macOS sweep makes (source-generated, safe under Native AOT).</summary>
internal static unsafe partial class MacNative
{
    public const int SignalKill = 9;
    public const int SignalStop = 17;
    public const int NoSuchProcess = 3;
    public const int KernelControl = 1;
    public const int ArgumentsMax = 8;
    public const int ProcessArguments = 49;
    public const int BsdInfo = 3;
    public const uint Zombie = 5;

    private const string Library = "/usr/lib/libSystem.dylib";

    /// <summary>Fills <paramref name="buffer"/> (<paramref name="size"/> bytes) with every process id; returns how many.</summary>
    [LibraryImport(Library, EntryPoint = "proc_listallpids", SetLastError = true)]
    public static partial int ListAllPids(int* buffer, int size);

    /// <summary>Fills <paramref name="buffer"/> (<paramref name="size"/> bytes) with the ids of <paramref name="parent"/>'s children; returns how many.</summary>
    [LibraryImport(Library, EntryPoint = "proc_listchildpids", SetLastError = true)]
    public static partial int ListChildPids(int parent, int* buffer, int size);

    /// <summary>Returns the number of bytes written, 0 on failure.</summary>
    [LibraryImport(Library, EntryPoint = "proc_pidinfo", SetLastError = true)]
    public static partial int ProcessInfo(int pid, int flavor, ulong argument, ProcBsdInfo* buffer, int size);

    [LibraryImport(Library, EntryPoint = "sysctl", SetLastError = true)]
    public static partial int Sysctl(int* name, uint length, byte* value, ref nuint valueLength, nint newValue, nuint newLength);

    [LibraryImport(Library, EntryPoint = "setpgid", SetLastError = true)]
    public static partial int SetProcessGroup(int pid, int group);

    [LibraryImport(Library, EntryPoint = "getpgrp")]
    public static partial int ProcessGroup();

    [LibraryImport(Library, EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int pid, int signal);
}
