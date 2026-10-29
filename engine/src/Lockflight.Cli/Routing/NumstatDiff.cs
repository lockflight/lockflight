namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads <c>git diff --numstat -z --no-renames</c> (<c>added\tremoved\tpath</c> per NUL-terminated record) for the one thing routing
/// takes from it: which paths git treats as binary (<c>-\t-</c>).
/// </summary>
internal static class NumstatDiff
{
    private const string BinaryCount = "-";

    public static IReadOnlySet<string> BinaryPaths(string output)
    {
        HashSet<string> binary = new(StringComparer.Ordinal);
        foreach (string record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.TrimStart('\n').Split('\t', 3);
            if (fields.Length == 3 && fields[0] == BinaryCount && fields[1] == BinaryCount)
            {
                binary.Add(fields[2]);
            }
        }

        return binary;
    }
}
