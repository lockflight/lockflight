using Lockflight.Cli.Init;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Warns when the generated GitHub workflow predates the CI judge: it runs the gates in one job and does not re-run the
/// red/green evidence. A file <c>init</c> wrote is never overwritten; <c>update --project --judge-workflow</c>, a person's command,
/// replaces it when Lockflight wrote it (<see cref="JudgeWorkflowMigration"/>). Null when the workflow is missing or already runs the judge.
/// </summary>
internal static class WorkflowJudgeCheck
{
    private const string Name = "workflow: CI judge";

    public static DoctorCheck? Check(string root)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) || !JudgeWorkflowMigration.IsGatesOnly(File.ReadAllText(path)))
        {
            return null;
        }

        return new DoctorCheck(
            Name,
            CheckStatus.Warn,
            $"{Templates.WorkflowPath} runs the gates only; the CI judge also re-runs each red commit, green at head and trace, and reports the lockflight/judge status context next to gates",
            "once the project pins a 0.3 engine on its main branch, run 'lockflight update --project --judge-workflow' in a branch (a person's command; with --dry-run it shows the diff) and merge it with a person's review (the gates check keeps its name). A file edited outside the lines Lockflight wrote is refused: then delete it and run 'lockflight init' instead");
    }
}
