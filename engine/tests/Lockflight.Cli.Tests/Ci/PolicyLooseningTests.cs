using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A pull request that loosens <c>lockflight.yaml</c> is named and FAILs the <c>policy</c> check, whatever its class and review;
/// a tighter or unchanged policy passes, and a base without a manifest has nothing to compare.
/// </summary>
public sealed class PolicyLooseningTests : IAsyncLifetime
{
    private const string BasePolicy = """
        version: 2
        protected:
          - src/Payments/**
          - deploy/**
        approvals:
          check: fail
        routing:
          preset: default
          fix:
            maxLines: 20
          structural: critical
          critical:
            - src/Payments/**
            - deploy/**
        """;

    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        baseCommit = await repository.Write(Manifest.FileName, BasePolicy + "\n")
            .Write("docs/notes.md", "notes\n")
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("013-policy", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ALoosenedPolicyFailsAndNamesEachWeakening()
    {
        string loosened = BasePolicy
            .Replace("check: fail", "check: report", StringComparison.Ordinal)
            .Replace("maxLines: 20", "maxLines: 300", StringComparison.Ordinal)
            .Replace("structural: critical", "structural: feature", StringComparison.Ordinal);
        await repository.Write(Manifest.FileName, loosened + "\n").CommitAsync("chore: policy", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.True((check.Level, check.Verdict) == (CheckLevel.Required, CheckVerdict.Fail), check.Why);
        Assert.Contains("routing.fix.maxLines: 20 → 300", check.Why, StringComparison.Ordinal);
        Assert.Contains("routing.structural: critical → feature", check.Why, StringComparison.Ordinal);
        Assert.Contains("approvals.check: fail → report", check.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovedProtectedAndCriticalPathsAreLoosenings()
    {
        string loosened = BasePolicy.Replace("  - src/Payments/**\n", string.Empty, StringComparison.Ordinal);
        await repository.Write(Manifest.FileName, loosened + "\n").CommitAsync("chore: policy", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.True(check.Verdict == CheckVerdict.Fail, check.Why);
        Assert.Contains("protected: removed src/Payments/**", check.Why, StringComparison.Ordinal);
        Assert.Contains("routing.critical: removed src/Payments/**", check.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManyLooseningsKeepTheWhyShortAndEveryOneInThePlan()
    {
        string[] paths = [.. Enumerable.Range(1, 15).Select(index => $"src/Services/Module{index:D2}/Internal/**")];
        string widened = BasePolicy.Replace("  - deploy/**\napprovals:", string.Concat(paths.Select(path => $"  - {path}\n")) + "  - deploy/**\napprovals:", StringComparison.Ordinal);
        await repository.CheckoutAsync("main", TestContext.Current.CancellationToken);
        await repository.Write(Manifest.FileName, widened + "\n").CommitAsync("chore: more protected", TestContext.Current.CancellationToken);
        baseCommit = await repository.HeadAsync(TestContext.Current.CancellationToken);
        await repository.CheckoutNewAsync("015-policy", TestContext.Current.CancellationToken);
        await repository.Write(Manifest.FileName, BasePolicy + "\n").CommitAsync("chore: policy", TestContext.Current.CancellationToken);

        JudgePlan plan = await PlanAsync();
        CheckResult check = JudgeChecks.Decide(plan, JudgeFindings.None).Single(result => result.Id == CheckId.Policy);

        Assert.Equal(CheckVerdict.Fail, check.Verdict);
        Assert.True(check.Why.Length <= CiEvidence.MaxTextLength, check.Why);
        Assert.Contains("looser than the base in 15 ways, each listed under policy.loosened", check.Why, StringComparison.Ordinal);
        Assert.Equal(paths.Select(path => $"protected: removed {path}"), plan.Loosened);
    }

    [Fact]
    public async Task ARemovedManifestIsALoosening()
    {
        repository.Delete(Manifest.FileName);
        await repository.CommitAsync("chore: drop policy", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.Equal(CheckVerdict.Fail, check.Verdict);
        Assert.Contains("lockflight.yaml removed", check.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATighterPolicyPasses()
    {
        string tighter = BasePolicy.Replace("maxLines: 20", "maxLines: 10", StringComparison.Ordinal);
        await repository.Write(Manifest.FileName, tighter + "\n").CommitAsync("chore: policy", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.Equal((CheckLevel.Required, CheckVerdict.Pass), (check.Level, check.Verdict));
    }

    [Fact]
    public async Task WithoutAManifestOnTheBaseTheCheckIsOff()
    {
        await repository.CheckoutAsync("main", TestContext.Current.CancellationToken);
        repository.Delete(Manifest.FileName);
        baseCommit = await repository.CommitAsync("chore: no policy", TestContext.Current.CancellationToken);
        await repository.CheckoutNewAsync("014-adopt", TestContext.Current.CancellationToken);
        await repository.Write(Manifest.FileName, "version: 2\n").CommitAsync("chore: adopt", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.Equal((CheckLevel.Off, CheckVerdict.NotApplicable), (check.Level, check.Verdict));
    }

    [Fact]
    public async Task AHeadThisEngineCannotReadIsNotEarnedNotFailed()
    {
        await repository.Write(Manifest.FileName, BasePolicy + "\nfuture: true\n").CommitAsync("chore: newer engine", TestContext.Current.CancellationToken);

        CheckResult check = await PolicyCheckAsync();

        Assert.Equal(CheckVerdict.NotEarned, check.Verdict);
        Assert.Contains("could not be compared", check.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void MovingFromVersionOneToVersionTwoLoosensNothing() =>
        Assert.Empty(PolicyLoosening.Between(Load("version: 1\n"), Load("version: 2\n")));

    [Fact]
    public void ReorderedListsLoosenNothing() =>
        Assert.Empty(PolicyLoosening.Between(
            Load("version: 2\nprotected:\n  - a/**\n  - b/**\nrouting:\n  preset: default\n  critical:\n    - a/**\n    - b/**\n"),
            Load("version: 2\nprotected:\n  - b/**\n  - a/**\nrouting:\n  preset: default\n  critical:\n    - b/**\n    - a/**\n")));

    [Fact]
    public void ARemovedUnitIsALoosening() =>
        Assert.Contains("units: removed svc/b", PolicyLoosening.Between(
            Load("version: 2\nunits:\n  - path: svc/a\n  - path: svc/b\n"),
            Load("version: 2\nunits:\n  - path: svc/a\n")));

    [Fact]
    public void PresetValuesCountLikeWrittenOnes()
    {
        Manifest before = Load("version: 2\nrouting:\n  preset: strict\n");
        Manifest after = Load("version: 2\nrouting:\n  preset: relaxed\n");

        Assert.NotEmpty(PolicyLoosening.Between(before, after));
        Assert.Empty(PolicyLoosening.Between(after, before));
    }

    private static Manifest Load(string yaml)
    {
        ManifestLoadResult result = new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(yaml);
        Assert.True(result.Manifest is not null, result.Error?.Describe());
        return result.Manifest;
    }

    private async Task<CheckResult> PolicyCheckAsync() =>
        JudgeChecks.Decide(await PlanAsync(), JudgeFindings.None).Single(result => result.Id == CheckId.Policy);

    private async Task<JudgePlan> PlanAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.HeadAsync(cancellationToken);
        JudgeInputs inputs = new(repository.Root, head, baseCommit, "013-policy", JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: false);
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, EnvironmentReader.Of(new Dictionary<string, string>()), cancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }
}
