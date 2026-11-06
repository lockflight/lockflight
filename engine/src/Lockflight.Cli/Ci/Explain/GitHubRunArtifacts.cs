using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// Downloads a run's Lockflight artifacts (the report's verdict and the judge's evidence, with any suffix a matrix gives them)
/// with the GitHub CLI: <c>gh run download</c>, signed in as the person, so it needs <c>gh</c> on PATH and <c>gh auth login</c>.
/// </summary>
internal static class GitHubRunArtifacts
{
    private const string Gh = "gh";
    private const string SignIn = "sign in with 'gh auth login' (or set GH_TOKEN) and run it again";

    private static readonly string[] Patterns = ["lockflight-verdict*", "lockflight-evidence*"];

    /// <summary>Downloads into <paramref name="directory"/>; null when it did, else what went wrong and what to do.</summary>
    public static async Task<string?> DownloadAsync(RunProcess processes, GitHubRun run, string directory, string workingDirectory, CancellationToken cancellationToken)
    {
        List<string> arguments = ["run", "download", run.Id, "--dir", directory];
        foreach (string pattern in Patterns)
        {
            arguments.AddRange(["--pattern", pattern]);
        }

        if (run.Repository is string repository)
        {
            arguments.AddRange(["--repo", repository]);
        }

        ProcessResult download;
        try
        {
            download = await processes(Gh, arguments, workingDirectory, cancellationToken);
        }
        catch (ExecutableNotFoundException)
        {
            return "the GitHub CLI (gh) is not on PATH: install it (https://cli.github.com), " + SignIn
                + "; or download the run's lockflight-verdict artifact yourself and pass its folder";
        }

        if (download.ExitCode == 0)
        {
            return null;
        }

        string said = download.Combined.Trim().ReplaceLineEndings(" ");
        if (said.Contains("auth login", StringComparison.OrdinalIgnoreCase) || said.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || said.Contains("HTTP 401", StringComparison.Ordinal))
        {
            return $"the GitHub CLI is not signed in: {SignIn} ({said})";
        }

        if (said.Contains("no valid artifacts", StringComparison.OrdinalIgnoreCase) || said.Contains("no artifact", StringComparison.OrdinalIgnoreCase))
        {
            return $"run {run.Id} has no lockflight-verdict or lockflight-evidence artifact: the judge did not run there, or its artifacts expired ({said})";
        }

        return $"'gh run download {run.Id}' failed (exit {download.ExitCode}): {said}";
    }
}
