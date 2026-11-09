using Lockflight.Cli.Init;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Init;

public sealed class JudgeWorkflowRefreshTests
{
    private const string ReleasedPin = "lockflight/lockflight/action@6960ea75096a0754fed65038d5557d690526172d # v0.3.1";

    private static readonly string[] TwoChannels = ["8.0.x", "10.0.x"];

    /// <summary>The trigger (by hand or not, and while a development build is pinned), the Windows runner and the workspace shell clone.</summary>
    public static TheoryData<bool, bool, bool, bool> Shapes()
    {
        TheoryData<bool, bool, bool, bool> shapes = [];
        foreach ((bool manual, bool developmentPin) in new[] { (false, false), (true, false), (true, true) })
        {
            foreach (bool windows in new[] { false, true })
            {
                foreach (bool shell in new[] { false, true })
                {
                    shapes.Add(manual, developmentPin, windows, shell);
                }
            }
        }

        return shapes;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheWorkflowThisEngineWritesIsCurrentInEveryShape(bool manual, bool developmentPin, bool windows, bool shell)
    {
        CiTrigger trigger = manual ? CiTrigger.Manual : CiTrigger.PullRequest;
        string workflow = Shaped(Templates.Workflow(TwoChannels, trigger, developmentPin), windows, shell);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(workflow);

        Assert.Equal(JudgeWorkflowRefreshStatus.Current, plan.Status);
        Assert.Equal(workflow, plan.Replacement);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AReleasedWorkflowGetsThisEnginesTemplateInTheSameShape(bool manual, bool developmentPin, bool windows, bool shell)
    {
        CiTrigger trigger = manual ? CiTrigger.Manual : CiTrigger.PullRequest;
        string released = Shaped(Released(TwoChannels, trigger, developmentPin), windows, shell);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(released);

        Assert.Equal(JudgeWorkflowRefreshStatus.Outdated, plan.Status);
        Assert.Equal(Shaped(Templates.Workflow(TwoChannels, trigger, developmentPin), windows, shell), plan.Replacement);
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheWorkflowEveryReleaseWroteIsRecognisedAndGetsThisEnginesTemplate(string version)
    {
        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(ReleasedFixtures.Read(version, "repository/.github/workflows/lockflight-gates.yml"));

        Assert.NotEqual(JudgeWorkflowRefreshStatus.Edited, plan.Status);
        Assert.Equal(Templates.Workflow([DotnetChannels.EngineChannel]), plan.Replacement);
    }

    [Fact]
    public void AnEditedWorkflowIsLeftForAPersonWithThisEnginesTemplateToCompare()
    {
        string edited = Released(TwoChannels, CiTrigger.PullRequest, developmentPin: false) + "\n# Our own note.\n";

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(edited);

        Assert.Equal(JudgeWorkflowRefreshStatus.Edited, plan.Status);
        Assert.Equal(Templates.Workflow(TwoChannels), plan.Replacement);
    }

    [Fact]
    public void AnEditedManualWorkflowIsComparedWithTheManualTemplate()
    {
        string edited = Templates.Workflow(TwoChannels, CiTrigger.Manual, developmentPin: false).Replace("timeout-minutes: 55", "timeout-minutes: 90", StringComparison.Ordinal);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(edited);

        Assert.Equal(JudgeWorkflowRefreshStatus.Edited, plan.Status);
        Assert.Equal(Templates.Workflow(TwoChannels, CiTrigger.Manual, developmentPin: false), plan.Replacement);
    }

    [Fact]
    public void ARunnerChosenByHandIsKeptInTheReplacement()
    {
        string chosen = JudgeRunner.WithRunsOn(Released(TwoChannels, CiTrigger.PullRequest, developmentPin: false), "judge", "[self-hosted, linux, big]");

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(chosen);

        Assert.Equal(JudgeWorkflowRefreshStatus.Outdated, plan.Status);
        Assert.Equal("[self-hosted, linux, big]", JudgeRunner.RunsOn(plan.Replacement!, "judge"));
        Assert.Equal(JudgeRunner.LinuxLabel, JudgeRunner.RunsOn(plan.Replacement!, "review"));
    }

    [Fact]
    public void WindowsLineEndingsAreKept()
    {
        string released = Released(TwoChannels, CiTrigger.PullRequest, developmentPin: false).Replace("\n", "\r\n", StringComparison.Ordinal);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(released);

        Assert.Equal(JudgeWorkflowRefreshStatus.Outdated, plan.Status);
        Assert.Equal(Templates.Workflow(TwoChannels).Replace("\n", "\r\n", StringComparison.Ordinal), plan.Replacement);
    }

    [Fact]
    public void APinRaisedByHandIsAnEditNeverLowered()
    {
        string raised = Templates.Workflow(TwoChannels).Replace(
            $"action@{Templates.ActionCommit} ", $"action@{new string('e', 40)} ", StringComparison.Ordinal);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(raised);

        Assert.Equal(JudgeWorkflowRefreshStatus.Edited, plan.Status);
    }

    [Fact]
    public void TheWorkflowBeforeTheFirstParentPolicyCheckGetsThisEnginesTemplate()
    {
        string before = Templates.WorkflowOf(ReleasedJudgeWorkflows.All[0], TwoChannels, CiTrigger.PullRequest, developmentPin: false);

        JudgeWorkflowRefreshPlan plan = JudgeWorkflowRefresh.Plan(before);

        Assert.Contains("merge-base --is-ancestor", before, StringComparison.Ordinal);
        Assert.Equal(JudgeWorkflowRefreshStatus.Outdated, plan.Status);
        Assert.Equal(Templates.Workflow(TwoChannels), plan.Replacement);
    }

    [Fact]
    public void AWorkflowThatRunsTheGatesAloneIsNotTheJudges()
    {
        Assert.Equal(JudgeWorkflowRefreshStatus.NotJudge, JudgeWorkflowRefresh.Plan(Templates.UnitWorkflow(TwoChannels)).Status);
    }

    private static string Released(string[] channels, CiTrigger trigger, bool developmentPin) =>
        Templates.WorkflowOf(ReleasedJudgeWorkflows.All[^1], channels, trigger, developmentPin)
            .Replace($"lockflight/lockflight/action@{Templates.ActionCommit} # {Templates.ActionVersion}", ReleasedPin, StringComparison.Ordinal);

    private static string Shaped(string workflow, bool windows, bool shell)
    {
        string runner = JudgeRunner.Apply(workflow, windows);
        return shell ? CiShellPointer.ForUnit(new CiFile(Templates.WorkflowPath, runner), CiPlatform.GitHub).Content : runner;
    }
}
