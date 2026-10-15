using System.Text;

namespace Lockflight.Cli.Proposals;

/// <summary>Lines a single-file patch adds and removes, as <c>git diff --stat</c> counts them.</summary>
internal sealed record DiffStat(int Added, int Removed)
{
    private const string HunkStart = "@@";

    public static DiffStat Of(byte[] patch)
    {
        int added = 0;
        int removed = 0;
        bool inHunks = false;
        foreach (string line in Encoding.UTF8.GetString(patch).Split('\n'))
        {
            inHunks = inHunks || line.StartsWith(HunkStart, StringComparison.Ordinal);
            if (inHunks && line.StartsWith('+'))
            {
                added++;
            }
            else if (inHunks && line.StartsWith('-'))
            {
                removed++;
            }
        }

        return new DiffStat(added, removed);
    }

    public override string ToString() => $"+{Added} -{Removed}";
}
