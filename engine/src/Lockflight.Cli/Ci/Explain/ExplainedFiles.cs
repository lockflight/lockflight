namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// The files <c>ci explain</c> reads from a folder (a run's downloaded artifacts, an evidence folder): the report's verdict if
/// there is one, else the judge's evidence, else the action's refusal, else the local judge's; every one of that kind, since a
/// matrix run leaves one per job.
/// </summary>
internal static class ExplainedFiles
{
    private const int MaxDepth = 2;

    private static readonly string[] ByPreference =
    [
        CiReportCommand.VerdictFileName, CiEvidence.FileName, CiReportCommand.RefusalFileName, CiEvidence.LocalFileName,
    ];

    public static IReadOnlyList<string> Find(string directory)
    {
        string[] files = [.. Under(directory, MaxDepth)];
        foreach (string name in ByPreference)
        {
            string[] found = [.. files.Where(file => Path.GetFileName(file) == name).Order(StringComparer.Ordinal)];
            if (found.Length > 0)
            {
                return found;
            }
        }

        return [];
    }

    private static IEnumerable<string> Under(string directory, int depth) =>
        Directory.EnumerateFiles(directory).Concat(depth == 0 ? [] : Directory.EnumerateDirectories(directory).SelectMany(child => Under(child, depth - 1)));
}
