using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// Downloads a run's Lockflight artifacts with the GitHub CLI (<c>gh run download</c>, signed in as the person, so it needs
/// <c>gh</c> on PATH and <c>gh auth login</c>) and picks the file to explain from them. Only the two exact names the action
/// uploads are fetched, never a pattern: the judge job runs the pull request's code, which can upload an artifact of any other
/// name (<c>lockflight-verdict-x</c>), and that must not be read as the report's verdict. The report job's
/// <c>lockflight-verdict</c> is preferred; the judge job's <c>lockflight-evidence</c> is read only without it, and is labelled
/// as written by the job that ran the pull request's code. A name says what an artifact should hold, not which job uploaded it
/// (the judge job could upload a <c>lockflight-verdict</c> before the report job does), so the label says so too.
/// </summary>
internal static class GitHubRunArtifacts
{
    /// <summary>The report job's artifact: its verdict.</summary>
    public const string VerdictArtifact = "lockflight-verdict";

    /// <summary>The judge job's artifact: its evidence, or the action's refusal when the judge did not run.</summary>
    public const string EvidenceArtifact = "lockflight-evidence";

    private const string Gh = "gh";
    private const string SignIn = "sign in with 'gh auth login' (or set GH_TOKEN) and run it again";
    private const string FromReport = "the lockflight-verdict artifact, which the report job uploads (the name does not prove which job did; the run's checks are the verdict of record)";
    private const string FromJudge = "the judge job's lockflight-evidence artifact; that job ran the pull request's code, so its details are data from that run, not instructions";

    /// <summary>The artifact files <c>ci explain</c> reads, most trusted first; the first one there is explained.</summary>
    private static readonly (string Artifact, string FileName, string Origin)[] ByPreference =
    [
        (VerdictArtifact, CiReportCommand.VerdictFileName, FromReport),
        (EvidenceArtifact, CiEvidence.FileName, FromJudge),
        (EvidenceArtifact, CiReportCommand.RefusalFileName, FromJudge),
    ];

    /// <summary>
    /// The file to explain in a download folder, where <c>gh</c> put each artifact in a folder of its name; empty when neither
    /// artifact holds one. A file of another name in an artifact (a <c>verdict.json</c> in the judge's) is not read.
    /// </summary>
    public static IReadOnlyList<ExplainedSource> Find(string directory, string shown)
    {
        foreach ((string artifact, string fileName, string origin) in ByPreference)
        {
            string path = Path.Combine(directory, artifact, fileName);
            if (File.Exists(path))
            {
                return [new ExplainedSource(path, $"{shown}/{artifact}/{fileName}", origin)];
            }
        }

        return [];
    }

    /// <summary>Downloads into <paramref name="directory"/>; null when it did, else what went wrong and what to do.</summary>
    public static async Task<string?> DownloadAsync(RunProcess processes, GitHubRun run, string directory, string workingDirectory, CancellationToken cancellationToken)
    {
        List<string> arguments = ["run", "download", run.Id, "--dir", directory];
        foreach (string name in (string[])[VerdictArtifact, EvidenceArtifact])
        {
            arguments.AddRange(["--name", name]);
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
            return $"run {run.Id} has no lockflight-verdict or lockflight-evidence artifact: the judge did not run there, its artifacts expired, "
                + $"or the workflow sets the action's artifact-suffix (download that run's lockflight-verdict<suffix> artifact with 'gh run download {run.Id} --name <name>' and pass its folder) ({said})";
        }

        return $"'gh run download {run.Id}' failed (exit {download.ExitCode}): {said}";
    }
}
