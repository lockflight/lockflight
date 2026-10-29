namespace Lockflight.Cli.Doctor;

/// <summary>
/// Warns about a GitHub workflow that runs on <c>pull_request_target</c> and checks out the pull request's head: that event runs
/// the base branch's workflow with its secrets and a write token, so building the pull request's code there hands both to it.
/// The CI judge runs on <c>pull_request</c> for that reason. Null when no workflow does it.
/// </summary>
internal static class WorkflowTriggerCheck
{
    private const string Name = "workflows: pull_request_target";

    private static readonly string[] HeadReferences =
        ["github.event.pull_request.head.sha", "github.event.pull_request.head.ref", "github.head_ref", "refs/pull/"];

    public static DoctorCheck? Check(string root)
    {
        string workflows = Path.Combine(root, ".github", "workflows");
        if (!Directory.Exists(workflows))
        {
            return null;
        }

        string[] risky = [.. Directory.EnumerateFiles(workflows)
            .Where(path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path) is string text && text.Contains("pull_request_target", StringComparison.Ordinal)
                && HeadReferences.Any(reference => text.Contains(reference, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        return risky.Length == 0
            ? null
            : new DoctorCheck(
                Name,
                CheckStatus.Warn,
                $"{string.Join(", ", risky)} {(risky.Length == 1 ? "runs" : "run")} on pull_request_target and {(risky.Length == 1 ? "checks" : "check")} out the pull request's head: "
                + "the base's secrets and write token then reach the pull request's code",
                "run jobs that build or test pull request code on pull_request; keep pull_request_target for jobs that only read data");
    }
}
