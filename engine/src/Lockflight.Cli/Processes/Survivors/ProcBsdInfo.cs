using System.Runtime.InteropServices;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary><c>struct proc_bsdinfo</c> from <c>&lt;sys/proc_info.h&gt;</c>: 136 bytes. The sweep reads the status, parent, user and start time.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProcBsdInfo
{
    public uint Flags;
    public uint Status;
    public uint ExitStatus;
    public uint Pid;
    public uint ParentPid;
    public uint Uid;
    public uint Gid;
    public uint RealUid;
    public uint RealGid;
    public uint SavedUid;
    public uint SavedGid;
    public uint Reserved;
    public fixed byte Command[16];
    public fixed byte Name[32];
    public uint OpenFiles;
    public uint ProcessGroup;
    public uint JobControlCount;
    public uint TerminalDevice;
    public uint TerminalProcessGroup;
    public int Nice;
    public ulong StartSeconds;
    public ulong StartMicroseconds;
}
