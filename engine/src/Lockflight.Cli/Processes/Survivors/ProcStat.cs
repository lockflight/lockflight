using System.Globalization;

namespace Lockflight.Cli.Processes.Survivors;

/// <summary>
/// The fields of a Linux <c>/proc/&lt;pid&gt;/stat</c> line the sweep reads: the process's parent, its state (<c>Z</c> for a zombie)
/// and its start time in clock ticks since boot, which tells a process from a later one that reuses its pid.
/// </summary>
internal sealed record ProcStat(int Pid, int ParentPid, char State, ulong StartTime)
{
    private const int StateField = 0;
    private const int ParentField = 1;
    private const int StartTimeField = 19;

    public bool IsZombie => State is 'Z' or 'X';

    /// <summary>The fields of <paramref name="line"/>; null when it is not a stat line. The command name may hold spaces and parentheses, so the fields are read after the last <c>)</c>.</summary>
    public static ProcStat? Parse(string line)
    {
        int open = line.IndexOf('(', StringComparison.Ordinal);
        int close = line.LastIndexOf(')');
        if (open <= 0 || close < open
            || !int.TryParse(line.AsSpan(0, open).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return null;
        }

        string[] fields = line[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > StartTimeField
            && fields[StateField].Length == 1
            && int.TryParse(fields[ParentField], NumberStyles.None, CultureInfo.InvariantCulture, out int parent)
            && ulong.TryParse(fields[StartTimeField], NumberStyles.None, CultureInfo.InvariantCulture, out ulong startTime)
            ? new ProcStat(pid, parent, fields[StateField][0], startTime)
            : null;
    }

    /// <summary>Every process in <paramref name="table"/> whose chain of parents leads to <paramref name="root"/>, not counting the root.</summary>
    public static IReadOnlyList<ProcStat> Descendants(IReadOnlyCollection<ProcStat> table, int root)
    {
        ILookup<int, ProcStat> children = table.ToLookup(process => process.ParentPid);
        List<ProcStat> found = [];
        Queue<int> parents = new([root]);
        HashSet<int> seen = [root];
        while (parents.TryDequeue(out int parent))
        {
            foreach (ProcStat child in children[parent].Where(child => seen.Add(child.Pid)))
            {
                found.Add(child);
                parents.Enqueue(child.Pid);
            }
        }

        return found;
    }
}
