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
        if (!JudgeWorkflowMigration.IsGatesOnlyAt(root))
        {
            return null;
        }

        return new DoctorCheck(
            Name,
            CheckStatus.Warn,
            $"{Templates.WorkflowPath} runs the gates only; the CI judge also re-runs each red commit, green at head and trace, and reports the lockflight/judge status context next to gates",
            JudgeWorkflowMigration.MoveAdvice);
    }
}
