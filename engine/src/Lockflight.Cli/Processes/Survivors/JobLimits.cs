using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>, flattened (its basic limits, I/O counters and memory limits in order): 144 bytes on
/// 64-bit Windows. The sweep sets only <see cref="LimitFlags"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JobLimits
{
    public long PerProcessUserTimeLimit;
    public long PerJobUserTimeLimit;
    public uint LimitFlags;
    public nuint MinimumWorkingSetSize;
    public nuint MaximumWorkingSetSize;
    public uint ActiveProcessLimit;
    public nuint Affinity;
    public uint PriorityClass;
    public uint SchedulingClass;
    public ulong ReadOperationCount;
    public ulong WriteOperationCount;
    public ulong OtherOperationCount;
    public ulong ReadTransferCount;
    public ulong WriteTransferCount;
    public ulong OtherTransferCount;
    public nuint ProcessMemoryLimit;
    public nuint JobMemoryLimit;
    public nuint PeakProcessMemoryUsed;
    public nuint PeakJobMemoryUsed;
}
