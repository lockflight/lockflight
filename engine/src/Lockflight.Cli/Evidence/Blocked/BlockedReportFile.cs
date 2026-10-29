using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>Where the engine leaves its latest blocked report in a clone: <see cref="BlockedReport.LocalRelativePath"/> (git-ignored).</summary>
internal static class BlockedReportFile
{
    public static string PathIn(string root) => Path.Combine(root, BlockedReport.LocalRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string WriteLocal(string root, BlockedReport report)
    {
        string path = PathIn(root);
        JsonFiles.Write(path, BlockedReportJson.Render(report));
        return path;
    }
}
