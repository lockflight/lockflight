using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The job object calls the Windows sweep makes (source-generated, safe under Native AOT).</summary>
internal static unsafe partial class WindowsNative
{
    public const int ExtendedLimitInformation = 9;
    public const int BasicProcessIdList = 3;
    public const uint KillOnJobClose = 0x2000;
    public const uint JobQuery = 0x0004;
    public const uint JobTerminate = 0x0008;
    public const int MoreData = 234;

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
}
