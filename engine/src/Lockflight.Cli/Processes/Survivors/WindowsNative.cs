using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The job object and process calls the Windows sweep makes (source-generated, safe under Native AOT).</summary>
internal static unsafe partial class WindowsNative
{
    public const int ExtendedLimitInformation = 9;
    public const int BasicProcessIdList = 3;
    public const uint KillOnJobClose = 0x2000;
    public const uint JobQuery = 0x0004;
    public const uint JobTerminate = 0x0008;
    public const int MoreData = 234;
    public const int AccessDenied = 5;
    public const uint ProcessTerminate = 0x0001;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint Synchronize = 0x0010_0000;
    public const uint WaitObject0 = 0;

    /// <summary>The pseudo-handle <c>GetCurrentProcess</c> returns.</summary>
    public static readonly nint CurrentProcess = -1;

    private const string Library = "kernel32.dll";

    [LibraryImport(Library, EntryPoint = "CreateJobObjectW", SetLastError = true)]
    public static partial nint CreateJobObject(nint attributes, nint name);

    [LibraryImport(Library, EntryPoint = "SetInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(nint job, int informationClass, ref JobLimits information, int length);

    [LibraryImport(Library, EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport(Library, EntryPoint = "QueryInformationJobObject", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryInformationJobObject(nint job, int informationClass, byte* information, int length, out int returned);

    [LibraryImport(Library, EntryPoint = "DuplicateHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DuplicateHandle(
        nint sourceProcess, nint source, nint targetProcess, out nint target, uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport(Library, EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(Library, EntryPoint = "OpenProcess", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [LibraryImport(Library, EntryPoint = "IsProcessInJob", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsProcessInJob(SafeProcessHandle process, nint job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [LibraryImport(Library, EntryPoint = "TerminateProcess", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport(Library, EntryPoint = "WaitForSingleObject", SetLastError = true)]
    public static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport(Library, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* name, ref int size);
}
