using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class WorkflowTriggerCheckTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-workflows-");

    [Fact]
    public void AWorkflowOnPullRequestTargetThatChecksOutTheHeadIsAWarning()
    {
        Write("pwn.yml", "on: pull_request_target\njobs:\n  x:\n    steps:\n      - uses: actions/checkout@v4\n        with:\n          ref: ${{ github.event.pull_request.head.sha }}\n");

        DoctorCheck? check = WorkflowTriggerCheck.Check(root.FullName);

        Assert.NotNull(check);
        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains(".github/workflows/pwn.yml runs on pull_request_target", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void PullRequestTargetThatReadsOnlyTheBaseAndPullRequestWorkflowsPass()
    {
        Write("label.yml", "on: pull_request_target\njobs:\n  x:\n    steps:\n      - run: echo labels\n");
        Write("ci.yml", "on: pull_request\njobs:\n  x:\n    steps:\n      - uses: actions/checkout@v4\n        with:\n          ref: ${{ github.event.pull_request.head.sha }}\n");

        Assert.Null(WorkflowTriggerCheck.Check(root.FullName));
    }

    [Fact]
    public void AGeneratedWorkflowThatOnlyRunsTheGatesIsAWarningUntilItRunsTheJudge()
    {
        Write("lockflight-gates.yml", "jobs:\n  gates:\n    steps:\n      - run: lockflight gate run --profile pr\n");

        Assert.Equal(CheckStatus.Warn, WorkflowJudgeCheck.Check(root.FullName)?.Status);

        Write("lockflight-gates.yml", Templates.Workflow(["10.0.x"]));

        Assert.Null(WorkflowJudgeCheck.Check(root.FullName));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, ".github", "workflows"));
        File.WriteAllText(Path.Combine(root.FullName, ".github", "workflows", name), text);
    }
}
