using System.Text.RegularExpressions;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// The CI judge workflow's hardening, read from the file: its report jobs check the artifacts against the digests the judge and
/// review jobs output (<c>verify-artifacts</c>) and take the policy commit from their own policy step, not from the judge job's
/// output alone; and its action pin is a commit an engine of this repository wrote (<see cref="ReleasedActionCommits"/>). A file
/// <c>update</c> recognised was brought up to date already; one a person edited keeps the old jobs until the person merges the
/// proposal <c>update --project</c> wrote. Null when there is no judge workflow or nothing to say.
/// </summary>
internal static partial class WorkflowReportCheck
{
    private const string Name = "workflow: CI judge report";
    private const string ReportMode = "mode: report";
    private const string VerifyArtifacts = "verify-artifacts: \"true\"";
    private const string ClaimedPolicy = "needs.judge.outputs.policy ||";

    private const string MergeAdvice =
        "a person runs 'lockflight update --project' and merges the proposal it writes to .lockflight/local/migration/"
        + Templates.WorkflowPath + " into the workflow (a file edited by hand is never replaced)";

    public static IReadOnlyList<DoctorCheck> Check(string root)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        List<DoctorCheck> checks = [];
        int reports = Regex.Count(text, Regex.Escape(ReportMode));
        if (reports > 0 && (Regex.Count(text, Regex.Escape(VerifyArtifacts)) < reports || text.Contains(ClaimedPolicy, StringComparison.Ordinal)))
        {
            checks.Add(new DoctorCheck(
                Name,
                CheckStatus.Warn,
                $"{Templates.WorkflowPath}'s report jobs read the artifacts by name or take the policy commit from the judge job's output: "
                + "a step that runs the pull request's code can then supply the evidence the report reads",
                MergeAdvice));
        }

        string[] unknown = [.. ActionPin().Matches(text).Select(pin => pin.Groups["commit"].Value).Where(commit => !ReleasedActionCommits.IsKnown(commit)).Distinct()];
        if (unknown.Length > 0)
        {
            checks.Add(new DoctorCheck(
                "workflow: action pin",
                CheckStatus.Warn,
                $"{Templates.WorkflowPath} pins the CI judge's action to {string.Join(", ", unknown.Select(commit => commit[..12]))}, which no Lockflight engine pins: "
                + "on pull_request the pinned action is the judge, so a pin moved in a pull request replaces every check",
                $"pin a commit a Lockflight release names, route .github/workflows/** to a code owner, and see {CiPipeline.GitHubGuideUrl} for a required workflow or ruleset"));
        }

        return checks;
    }

    [GeneratedRegex(@"lockflight/lockflight/action@(?<commit>[0-9a-f]{40})")]
    private static partial Regex ActionPin();
}
