using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>The red commits the judge finds in real git histories, for each shape a pull request's history can take.</summary>
public sealed class JudgeHistoryTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        baseCommit = await repository.Write("README.md", "# shop\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("012-checkout", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task OnlyRealTrailersMarkARedNotALineInTheBody()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string red = await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.Write("b.cs", "b").CommitAsync("feat: US1", cancellationToken, "Lockflight-Red: 2\n\nmore text after it");

        RedCommitSelection selection = await SelectAsync(cancellationToken);

        Assert.Equal([red], selection.Cycles.Select(cycle => cycle.Commit));
    }

    [Fact]
    public async Task ARebaseKeepsTheTrailersOnTheNewCommits()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.CheckoutAsync("main", cancellationToken);
        baseCommit = await repository.Write("other.md", "x").CommitAsync("docs on main", cancellationToken);
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        await repository.RebaseAsync("main", cancellationToken);

        RedCommitSelection selection = await SelectAsync(cancellationToken);

        RedCommit red = Assert.Single(selection.Cycles);
        Assert.Equal(await repository.HeadAsync(cancellationToken), red.Commit);
    }

    [Fact]
    public async Task ARedMadeBeforeAMergeOfTheTargetKeepsItsOwnForkPoint()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string red = await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        string oldBase = baseCommit;
        await repository.CheckoutAsync("main", cancellationToken);
        baseCommit = await repository.Write("other.md", "x").CommitAsync("docs on main", cancellationToken);
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        await repository.MergeAsync("main", cancellationToken);
        string secondRed = await repository.Write("b.cs", "b").CommitAsync("test: red for US2", cancellationToken, null, "Lockflight-Red: 2");

        JudgePlan plan = await PlanAsync(cancellationToken);

        Assert.Equal(baseCommit, plan.MergeBase);
        Assert.Equal(oldBase, plan.Reds.Cycles.Single(cycle => cycle.Commit == red).ForkPoint);
        Assert.Equal(baseCommit, plan.Reds.Cycles.Single(cycle => cycle.Commit == secondRed).ForkPoint);
        Assert.Contains(plan.Steps, step => step.Id == "P2" && step.Commit == oldBase);
    }

    [Fact]
    public async Task ACherryPickedRedWithTheSameLabelSupersedesTheEarlierOne()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutNewAsync("other-pr", cancellationToken);
        string elsewhere = await repository.Write("c.cs", "c").CommitAsync("test: red elsewhere", cancellationToken, null, "Lockflight-Red: 1");
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        string first = await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        string picked = await repository.CherryPickAsync(elsewhere, cancellationToken);

        RedCommitSelection selection = await SelectAsync(cancellationToken);

        Assert.Equal([picked], selection.Cycles.Select(cycle => cycle.Commit));
        Assert.True(selection.All.Single(red => red.Commit == first).Superseded);
    }

    [Fact]
    public async Task ARevertedRedIsStillJudged()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string red = await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.RevertAsync(red, cancellationToken);

        Assert.Equal([red], (await SelectAsync(cancellationToken)).Cycles.Select(cycle => cycle.Commit));
    }

    [Fact]
    public async Task AForcePushThatDropsTheRedLeavesNoCycle()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string kept = await repository.Write("a.cs", "a").CommitAsync("feat: code", cancellationToken);
        await repository.Write("b.cs", "b").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.ResetAsync(kept, cancellationToken);

        Assert.Empty((await SelectAsync(cancellationToken)).Cycles);
    }

    [Fact]
    public async Task ARedOnASideBranchMergedIntoThePullRequestCounts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutNewAsync("side", cancellationToken);
        string red = await repository.Write("a.cs", "a").CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.CheckoutAsync("012-checkout", cancellationToken);
        await repository.Write("b.cs", "b").CommitAsync("chore: other work", cancellationToken);
        await repository.MergeAsync("side", cancellationToken);

        Assert.Equal([red], (await SelectAsync(cancellationToken)).Cycles.Select(cycle => cycle.Commit));
    }

    private async Task<RedCommitSelection> SelectAsync(CancellationToken cancellationToken) => (await PlanAsync(cancellationToken)).Reds;

    private async Task<JudgePlan> PlanAsync(CancellationToken cancellationToken)
    {
        string head = await repository.HeadAsync(cancellationToken);
        JudgeInputs inputs = new(repository.Root, head, baseCommit, "012-checkout", JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: true);
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, EnvironmentReader.Of(new Dictionary<string, string>()), cancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }
}
