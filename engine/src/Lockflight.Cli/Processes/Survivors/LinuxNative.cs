using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The libc calls the Linux sweep makes (source-generated, safe under Native AOT).</summary>
internal static partial class LinuxNative
{
    public const int SetChildSubreaper = 36;
    public const int SignalKill = 9;
    public const int SignalStop = 19;
    public const int NoHang = 1;

    private const string Library = "libc";

    /// <summary><c>prctl</c> takes a variable argument list; five integer arguments go in the same registers on x64 and arm64 Linux.</summary>
    [LibraryImport(Library, EntryPoint = "prctl", SetLastError = true)]
    public static partial int Prctl(int option, nuint argument2, nuint argument3, nuint argument4, nuint argument5);

    [LibraryImport(Library, EntryPoint = "kill", SetLastError = true)]
    public static partial int Kill(int pid, int signal);

    [LibraryImport(Library, EntryPoint = "waitpid", SetLastError = true)]
    public static partial int WaitPid(int pid, out int status, int options);
}
