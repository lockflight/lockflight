using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Warns when the generated GitHub workflow predates the CI judge: it runs the gates in one job and does not re-run the
/// red/green evidence. A file <c>init</c> wrote is never overwritten, so the fix is to regenerate it in a branch. Null when the
/// workflow is missing or already runs the judge.
/// </summary>
internal static class WorkflowJudgeCheck
{
    private const string Name = "workflow: CI judge";

    public static DoctorCheck? Check(string root)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || File.ReadAllText(path) is not string text
            || text.Contains("mode: judge", StringComparison.Ordinal) || !text.Contains("lockflight gate run", StringComparison.Ordinal))
        {
            return null;
        }

        return new DoctorCheck(
            Name,
            CheckStatus.Warn,
            $"{Templates.WorkflowPath} runs the gates only; the CI judge also re-runs each red commit, green at head and trace, and reports the lockflight/judge status context next to gates",
            $"in a branch, delete {Templates.WorkflowPath}, run 'lockflight init' to write the judge's workflow, and merge it with a person's review (the gates check keeps its name)");
    }
}
