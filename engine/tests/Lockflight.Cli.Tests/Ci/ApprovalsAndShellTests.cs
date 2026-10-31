using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>The approvals check, the spec diff and the workspace shell in the judge: git only, nothing is built.</summary>
public sealed class ApprovalsAndShellTests : IAsyncLifetime
{
    private const string ShellUrl = "https://example.com/shell.git";
    private const string UnitManifest = $"version: 2\nshell:\n  url: {ShellUrl}\n  unit: services/orders\n";
    private const string Owners = "/specs/ @org/product\n";

    private GitHistoryFixture repository = null!;
    private GitHistoryFixture shell = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        shell = await GitHistoryFixture.CreateAsync(cancellationToken);
        await shell.GitAsync(["remote", "add", "origin", ShellUrl], cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        shell.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task CodeownersOnTheTargetBranchThatOwnsTheSpecsPassesTheApprovalsCheck()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\n", Owners, specOnBase: false, editSpec: false);

        ApprovalsJudgement approvals = await DecideAsync(plan);

        Assert.Equal(CheckVerdict.Pass, approvals.Verdict);
        Assert.Contains("the base branch's .github/CODEOWNERS gives specs/** an owner", approvals.Why, StringComparison.Ordinal);
        Assert.Null(approvals.Blocked);
    }

    [Fact]
    public async Task AFeaturePullRequestThatChangesNoSpecAndHasNoneMergedWaitsForAnApproval()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string main = await repository.Write("lockflight.yaml", "version: 2\n").Write(".github/CODEOWNERS", Owners).Write("src/A.cs", "class A;\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
        string head = await repository.Write("src/B.cs", "class B;\n").CommitAsync("feat(001-shipping): US1", cancellationToken);
        JudgePlan plan = await PlanAsync(head, main, "001-shipping", shellPath: null);

        ApprovalsJudgement approvals = await DecideAsync(plan);
        JsonObject blocked = Blocked(plan, approvals)!;

        Assert.Equal(CheckVerdict.NotEarned, approvals.Verdict);
        Assert.Contains("no person approved this behaviour", approvals.Why, StringComparison.Ordinal);
        Assert.Equal("approval-missing", blocked["reason"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task AFeaturePullRequestThatChangesNoSpecPassesOnTheSpecMergedBeforeAndSaysSo()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\n", Owners, specOnBase: true, editSpec: false);

        ApprovalsJudgement approvals = await DecideAsync(plan);

        Assert.Equal(CheckVerdict.Pass, approvals.Verdict);
        Assert.Contains("changes no spec and relies on specs/001-shipping/spec.md, approved when it merged", approvals.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutCodeownersTheApprovalsCheckWaitsForAMaintainer()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\n", owners: null, specOnBase: false, editSpec: false);

        ApprovalsJudgement approvals = await DecideAsync(plan);
        JsonObject blocked = Blocked(plan, approvals)!;

        Assert.Equal(CheckVerdict.NotEarned, approvals.Verdict);
        Assert.Equal("codeowners-missing", blocked["reason"]!["code"]!.GetValue<string>());
        Assert.Equal("maintainer", blocked["needs"]![0]!["who"]!.GetValue<string>());
        Assert.Equal(".github/CODEOWNERS", blocked["needs"]![0]!["paths"]![0]!.GetValue<string>());
        Assert.Null(blocked["needs"]![0]!["command"]);
    }

    [Fact]
    public async Task SpecFirstWithTheSpecNotOnTheTargetBranchIsNotEarnedUntilAPersonMergesIt()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\napprovals:\n  default: spec-first\n", Owners, specOnBase: false, editSpec: false);

        ApprovalsJudgement approvals = await DecideAsync(plan);
        JsonObject blocked = Blocked(plan, approvals)!;

        Assert.Equal(CheckVerdict.NotEarned, approvals.Verdict);
        Assert.Contains("specs/001-shipping/spec.md is not on the base branch", approvals.Why, StringComparison.Ordinal);
        Assert.Equal("spec-not-on-base", blocked["reason"]!["code"]!.GetValue<string>());
        Assert.Equal("001-shipping", blocked["feature"]!.GetValue<string>());
        Assert.Equal("codeowner", blocked["needs"]![0]!["who"]!.GetValue<string>());
    }

    [Fact]
    public async Task SpecFirstWithTheSpecChangedInTheCodePullRequestFails()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\napprovals:\n  default: spec-first\n", Owners, specOnBase: true, editSpec: true);

        ApprovalsJudgement approvals = await DecideAsync(plan);

        Assert.Equal(CheckVerdict.Fail, approvals.Verdict);
        Assert.Null(Blocked(plan, approvals));
    }

    [Fact]
    public async Task SpecFirstWithTheSpecMergedFirstAndUnchangedPasses()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\napprovals:\n  default: spec-first\n", Owners, specOnBase: true, editSpec: false);

        ApprovalsJudgement approvals = await DecideAsync(plan);

        Assert.Equal(CheckVerdict.Pass, approvals.Verdict);
        Assert.Contains("spec-first: specs/001-shipping/spec.md was merged first and is unchanged", approvals.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithApprovalsCheckReportTheProblemsAreListedButNeverDecideTheVerdict()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\napprovals:\n  check: report\n", owners: null, specOnBase: false, editSpec: false);
        plan = plan with { Levels = plan.Levels.ToDictionary(level => level.Key, level => level.Key == CheckId.Approvals ? CheckLevel.Required : level.Value) };

        CheckResult check = JudgeChecks.Decide(plan, JudgeFindings.None with { Approvals = await DecideAsync(plan) }).Single(result => result.Id == CheckId.Approvals);

        Assert.Equal(CheckLevel.Advisory, check.Level);
        Assert.Equal(CheckVerdict.NotEarned, check.Verdict);
    }

    [Fact]
    public async Task TheSpecDiffHoldsOnlyTheSpecPathsThePullRequestChanges()
    {
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\n", Owners, specOnBase: true, editSpec: true);

        SpecDiff diff = (await SpecDiffReader.ReadAsync(plan, TestContext.Current.CancellationToken))!;

        Assert.Equal(["specs/001-shipping/spec.md"], diff.Paths);
        Assert.Contains("+- **FR-002**: Shipping MUST be free above 100 kg", diff.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("src/", diff.Text, StringComparison.Ordinal);
        Assert.False(diff.Truncated);
    }

    [Fact]
    public async Task AUnitIsJudgedAgainstTheShellsSpecOnceItIsMerged()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string shellCommit = await ShellAsync(withSpec: true);
        JudgePlan plan = await UnitPullRequestAsync("012-checkout", UnitManifest + "approvals:\n  default: spec-first\n");

        ShellPolicyReading reading = await JudgeShell.ReadAsync(plan, cancellationToken);
        plan = plan with { Shell = reading.Policy };
        SpecResolution spec = await ShellSpecSource.ResolveAsync(plan, plan.Shell!, "012-checkout", [], cancellationToken);
        ApprovalsJudgement approvals = await DecideAsync(plan);

        Assert.Null(reading.Error);
        Assert.Equal(SpecOrigin.Shell, spec.Origin);
        Assert.Equal(shellCommit, spec.ShellCommit);
        Assert.Equal(ShellUrl, spec.ShellUrl);
        Assert.Contains("FR-001", spec.SpecText, StringComparison.Ordinal);
        Assert.Null(spec.Problem);
        Assert.Equal(CheckVerdict.Pass, approvals.Verdict);
        Assert.Contains("the workspace shell's CODEOWNERS", approvals.Why, StringComparison.Ordinal);
        Assert.Null(await SpecDiffReader.ReadAsync(plan, cancellationToken));
    }

    [Fact]
    public async Task AUnitPullRequestWaitsUntilTheShellsSpecMerges()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ShellAsync(withSpec: false);
        JudgePlan plan = await UnitPullRequestAsync("feature/012-checkout", UnitManifest + "approvals:\n  default: spec-first\n");
        plan = plan with { Shell = (await JudgeShell.ReadAsync(plan, cancellationToken)).Policy };

        SpecResolution spec = await ShellSpecSource.ResolveAsync(plan, plan.Shell!, "feature/012-checkout", [], cancellationToken);
        ApprovalsJudgement approvals = await DecideAsync(plan);
        JsonObject blocked = Blocked(plan, null, new TraceJudgement(spec, CheckVerdict.NotEarned, spec.Problem!, Applies: true, []), CheckId.Trace)!;

        Assert.Equal("012-checkout", spec.FeatureId);
        Assert.Null(spec.SpecText);
        Assert.Contains("merge the feature's spec pull request in the shell first", spec.Problem, StringComparison.Ordinal);
        Assert.Equal(CheckVerdict.NotEarned, approvals.Verdict);
        Assert.Contains("is not on the workspace shell's branch", approvals.Why, StringComparison.Ordinal);
        Assert.Equal("spec-not-on-base", blocked["reason"]!["code"]!.GetValue<string>());
        Assert.Contains(ShellUrl, blocked["needs"]![0]!["detail"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUnitWithoutItsShellCloneIsNotJudged()
    {
        await ShellAsync(withSpec: true);
        JudgePlan plan = await UnitPullRequestAsync("012-checkout", UnitManifest, shellPath: null);

        ShellPolicyReading reading = await JudgeShell.ReadAsync(plan, TestContext.Current.CancellationToken);

        Assert.Null(reading.Policy);
        Assert.Contains("lockflight workspace clone --into <dir>", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShellCloneOfAnotherRepositoryIsRefused()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ShellAsync(withSpec: true);
        await shell.GitAsync(["remote", "set-url", "origin", "https://example.com/other.git"], cancellationToken);
        JudgePlan plan = await UnitPullRequestAsync("012-checkout", UnitManifest);

        ShellPolicyReading reading = await JudgeShell.ReadAsync(plan, cancellationToken);

        Assert.Null(reading.Policy);
        Assert.Contains("is a clone of https://example.com/other.git", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShellCloneForTheRepositoryThatOnlyNowBecomesAUnitIsIgnored()
    {
        await ShellAsync(withSpec: true);
        JudgePlan plan = await UnitPullRequestAsync("012-checkout", "version: 2\n");

        ShellPolicyReading reading = await JudgeShell.ReadAsync(plan, TestContext.Current.CancellationToken);

        Assert.Null(reading.Policy);
        Assert.Null(reading.Error);
    }

    [Fact]
    public async Task ASpecFirstPathMakesTheCheckRequiredWhateverTheClassesLevel()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JudgePlan plan = await FeaturePullRequestAsync("version: 2\napprovals:\n  specFirstPaths: [\"src/**\"]\n", Owners, specOnBase: false, editSpec: false);

        ApprovalsJudgement approvals = (await ApprovalsJudge.DecideAsync(plan, JudgeFindings.None, cancellationToken))!;
        CheckResult check = JudgeChecks.Decide(plan, JudgeFindings.None with { Approvals = approvals }).Single(result => result.Id == CheckId.Approvals);

        Assert.Contains("specFirstPaths src/** (src/B.cs)", approvals.Facts.SpecFirst.Because);
        Assert.Equal((CheckLevel.Required, CheckVerdict.NotEarned), (check.Level, check.Verdict));
    }

    [Fact]
    public async Task SpecFirstIsNotSatisfiedByTheOnlySpecThereIsWhenTheBranchNamesNoFeature()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string main = await repository.Write("lockflight.yaml", "version: 2\napprovals:\n  default: spec-first\n").Write(".github/CODEOWNERS", Owners)
            .Write("specs/001-shipping/spec.md", "# Shipping\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("quick-change", cancellationToken);
        string head = await repository.Write("src/B.cs", "class B;\n").CommitAsync("feat: change", cancellationToken);

        ApprovalsJudgement approvals = await DecideAsync(await PlanAsync(head, main, "quick-change", shellPath: null));

        Assert.Equal(CheckVerdict.NotEarned, approvals.Verdict);
        Assert.Contains("no feature spec was found", approvals.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABranchAndARedCommitThatDisagreeInAUnitWaitForNoPerson()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ShellAsync(withSpec: true);
        JudgePlan plan = await UnitPullRequestAsync("012-checkout", UnitManifest);
        plan = plan with { Shell = (await JudgeShell.ReadAsync(plan, cancellationToken)).Policy };
        SpecResolution disagreeing = new("012-checkout", "branch", null, null, null, SpecOrigin.Shell, "the branch and the tests disagree on the feature");

        Assert.Null(Blocked(plan, null, new TraceJudgement(disagreeing, CheckVerdict.NotEarned, "disagree", Applies: true, []), CheckId.Trace));
    }

    /// <summary>A pull request on 001-shipping that adds code and, unless it is on the base already, the feature's spec.</summary>
    private async Task<JudgePlan> FeaturePullRequestAsync(string manifest, string? owners, bool specOnBase, bool editSpec)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Spec = "# Shipping\n\n## Requirements\n\n- **FR-001**: Shipping MUST cost twice the weight\n";
        repository.Write("lockflight.yaml", manifest).Write("src/A.cs", "class A;\n");
        if (owners is not null)
        {
            repository.Write(".github/CODEOWNERS", owners);
        }

        if (specOnBase)
        {
            repository.Write("specs/001-shipping/spec.md", Spec);
        }

        string main = await repository.CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
        repository.Write("src/B.cs", "class B;\n");
        if (!specOnBase || editSpec)
        {
            repository.Write("specs/001-shipping/spec.md", Spec + (editSpec ? "- **FR-002**: Shipping MUST be free above 100 kg\n" : string.Empty));
        }

        string head = await repository.CommitAsync("feat(001-shipping): US1", cancellationToken);
        return await PlanAsync(head, main, "001-shipping", shellPath: null);
    }

    private async Task<JudgePlan> UnitPullRequestAsync(string branch, string manifest, string? shellPath = "")
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string main = await repository.Write("lockflight.yaml", manifest).Write("src/A.cs", "class A;\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync(branch, cancellationToken);
        string head = await repository.Write("src/B.cs", "class B;\n").CommitAsync("feat(012-checkout): US1", cancellationToken);
        return await PlanAsync(head, main, branch, shellPath == string.Empty ? shell.Root : shellPath);
    }

    private async Task<string> ShellAsync(bool withSpec)
    {
        shell.Write("lockflight.yaml", "version: 2\nunits:\n  - path: services/orders\n").Write("CODEOWNERS", Owners);
        if (withSpec)
        {
            shell.Write("specs/012-checkout/spec.md", "# Checkout\n\n## Requirements\n\n- **FR-001**: Checkout MUST charge the card\n");
        }

        return await shell.CommitAsync("shell", TestContext.Current.CancellationToken);
    }

    private async Task<JudgePlan> PlanAsync(string head, string baseCommit, string branch, string? shellPath)
    {
        JudgeInputs inputs = new(repository.Root, head, baseCommit, branch, JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), shellPath, ReviewExpected: false);
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, EnvironmentReader.Of(new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }

    private static async Task<ApprovalsJudgement> DecideAsync(JudgePlan plan)
    {
        plan = plan with { Levels = plan.Levels.ToDictionary(level => level.Key, level => level.Key == CheckId.Approvals ? CheckLevel.Required : level.Value) };
        ApprovalsJudgement? approvals = await ApprovalsJudge.DecideAsync(plan, JudgeFindings.None, TestContext.Current.CancellationToken);
        Assert.NotNull(approvals);
        return approvals;
    }

    /// <summary>The blocked report of a run whose only check is <paramref name="check"/>, required, with the given findings.</summary>
    private static JsonObject? Blocked(JudgePlan plan, ApprovalsJudgement? approvals, TraceJudgement? trace = null, CheckId check = CheckId.Approvals)
    {
        CheckVerdict verdict = check == CheckId.Approvals ? approvals!.Verdict : trace!.Verdict;
        JudgeRun run = new(plan, JudgeFindings.None with { Approvals = approvals, Trace = trace }, [new CheckResult(check, CheckLevel.Required, verdict, "why")], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return JudgeBlocked.For(run) is BlockedReport report ? BlockedReportJson.Render(report) : null;
    }
}
