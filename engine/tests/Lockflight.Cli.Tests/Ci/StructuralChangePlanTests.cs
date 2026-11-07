using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A structural-only change is planned from git alone (no build): red and tests since red off, trace and the existing tests
/// required, and the report job, re-planning, accepts that evidence only where git makes the pull request one.
/// </summary>
public sealed class StructuralChangePlanTests : IAsyncLifetime
{
    private const string Branch = "012-pricing";
    private const string StructuralSpec = "# Feature Specification: Pricing module\n\n## Requirements *(mandatory)*\n\n- **FR-010** [structural]: Order totals MUST stay as they are after the pricing code moves\n";

    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;
    private string head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        repository.Write("lockflight.yaml", "version: 1\n").Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n")
            .Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Total(decimal net) => net * 1.2m;\n}\n");
        await repository.CommitAsync("init", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AnAllStructuralSpecChangedUnderCodeownersPlansNoRed()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec);

        JudgePlan plan = await PlanAsync();

        Assert.True(plan.InStructuralLane);
        Assert.Equal(StructuralRoute.Codeowners, plan.StructuralChange!.Route);
        Assert.Equal(CheckLevel.Off, plan.Levels[CheckId.Red]);
        Assert.Equal(CheckLevel.Off, plan.Levels[CheckId.TestsSinceRed]);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Trace]);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.TestsExistingChanged]);
        Assert.DoesNotContain(plan.Steps, step => step.Id == "P3");
    }

    [Fact]
    public async Task RedIsNotApplicableWithItsReasonInAStructuralChange()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec);

        CheckResult red = JudgeChecks.Decide(await PlanAsync(), JudgeFindings.None).Single(check => check.Id == CheckId.Red);

        Assert.Equal(CheckVerdict.NotApplicable, red.Verdict);
        Assert.Equal(JudgeChecks.StructuralLaneWhy, red.Why);
    }

    [Fact]
    public async Task WithoutCodeownersTheStructuralSpecStillNeedsARedAndSaysWhy()
    {
        await PullRequestAsync(codeowners: false, StructuralSpec);

        JudgePlan plan = await PlanAsync();
        CheckResult red = JudgeChecks.Decide(plan, JudgeFindings.None).Single(check => check.Id == CheckId.Red);

        Assert.False(plan.InStructuralLane);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
        Assert.Equal(ReasonCode.RedMissing, red.Reason);
        Assert.Contains("the all-[structural] spec does not waive it", red.Why, StringComparison.Ordinal);
        Assert.Contains("no CODEOWNERS file", red.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABehaviourChangeMarkedStructuralInAMixedSpecGetsNothing()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec + "- **FR-011**: Totals MUST include the delivery fee\n");

        JudgePlan plan = await PlanAsync();

        Assert.Null(plan.StructuralChange);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task ARedCommitKeepsRedGreen()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await PullRequestAsync(codeowners: true, StructuralSpec);
        head = await repository.Write("src/Shop/Note.cs", "namespace Shop;\n\ninternal static class Note;\n").CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");

        JudgePlan plan = await PlanAsync();

        Assert.False(plan.InStructuralLane);
        Assert.Equal(StructuralCondition.NoRedCommit, Assert.Single(plan.StructuralChange!.Unmet).Condition);
        Assert.Equal(CheckLevel.Required, plan.Levels[CheckId.Red]);
    }

    [Fact]
    public async Task AConfigurationChangeIsNoStructuralChange()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await PullRequestAsync(codeowners: true, StructuralSpec);
        head = await repository.Write("src/Shop/appsettings.json", "{ \"Tax\": 0.25 }\n").CommitAsync("chore: settings", cancellationToken);

        JudgePlan plan = await PlanAsync();

        Assert.False(plan.InStructuralLane);
        Assert.Contains(plan.StructuralChange!.Unmet, unmet => unmet.Condition == StructuralCondition.NoBehaviourSignal);
    }

    [Fact]
    public async Task TheReportAcceptsStructuralChangeEvidenceGitAgreesWith()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec);

        Assert.Null(await MismatchAsync(await EvidenceAsync()));
    }

    [Fact]
    public async Task TheReportRefusesRedTurnedOffWhereGitMakesNoStructuralChange()
    {
        await PullRequestAsync(codeowners: false, StructuralSpec);
        JsonObject evidence = await EvidenceAsync();
        JsonNode red = evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!;
        red["requirement"] = "off";
        red["verdict"] = "N/A";

        Assert.Contains("it has red off, and the plan makes it required", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportRefusesAForgedStructuralChangeClaim()
    {
        await PullRequestAsync(codeowners: false, StructuralSpec);
        JsonObject evidence = await EvidenceAsync();
        evidence["structuralChange"] = new JsonObject { ["eligible"] = true };

        Assert.Contains("claims a structural-only change", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportRefusesTraceTurnedOffInAStructuralChange()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec);
        JsonObject evidence = await EvidenceAsync();
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "trace")!["requirement"] = "advisory";

        Assert.Contains("it has trace advisory, and the plan makes it required", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanShowsTheStructuralChange()
    {
        await PullRequestAsync(codeowners: true, StructuralSpec);

        JsonObject printed = CiEvidence.Plan(await PlanAsync());

        Assert.True(printed["structuralChange"]!["eligible"]!.GetValue<bool>());
        Assert.Equal("codeowners", printed["structuralChange"]!["route"]!.GetValue<string>());
        Assert.Equal("off", printed["checks"]!["red"]!.GetValue<string>());
    }

    private async Task PullRequestAsync(bool codeowners, string spec)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        if (codeowners)
        {
            await repository.Write(".github/CODEOWNERS", "specs/** @team\n").CommitAsync("chore: code owners", cancellationToken);
        }

        baseCommit = await repository.HeadAsync(cancellationToken);
        await repository.CheckoutNewAsync(Branch, cancellationToken);
        head = await repository.Write("specs/012-pricing/spec.md", spec)
            .Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    private const decimal Tax = 1.2m;\n\n    public decimal Total(decimal net) => net * Tax;\n}\n")
            .CommitAsync("refactor: name the tax rate", cancellationToken);
    }

    private JudgeInputs Inputs() =>
        new(repository.Root, head, baseCommit, Branch, JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: true);

    private async Task<JudgePlan> PlanAsync()
    {
        JudgePlanning planning = await JudgePlanner.PlanAsync(Inputs(), EnvironmentReader.Of(new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }

    /// <summary>Evidence as the judge writes it for the plan: its levels, every check passing unless git alone decides it.</summary>
    private async Task<JsonObject> EvidenceAsync()
    {
        JudgePlan plan = await PlanAsync();
        return new JsonObject
        {
            ["schema"] = CiEvidence.Schema,
            ["engine"] = new JsonObject { ["version"] = ToolPin.RunningVersion },
            ["event"] = "pr",
            ["head"] = head,
            ["policy"] = new JsonObject { ["from"] = plan.Policy.Commit },
            ["changeClass"] = new JsonObject { ["class"] = ChangeClassNames.ToName(plan.Class.Classification.Class) },
            ["redCommits"] = new JsonArray([.. plan.Reds.All.Select(red => (JsonNode)new JsonObject { ["commit"] = red.Commit })]),
            ["structuralChange"] = plan.StructuralChange is null ? null : new JsonObject { ["eligible"] = plan.InStructuralLane },
            ["checks"] = new JsonArray([.. plan.Levels.Select(level => (JsonNode)new JsonObject
            {
                ["id"] = CheckIdNames.ToName(level.Key),
                ["verdict"] = level.Value == CheckLevel.Off ? "N/A" : CheckVerdictNames.ToName(EvidenceCrossCheck.PlannedVerdict(plan, level.Key) ?? CheckVerdict.Pass),
                ["requirement"] = CheckLevelNames.ToName(level.Value),
                ["why"] = "ok",
            })]),
        };
    }

    private Task<string?> MismatchAsync(JsonObject evidence) =>
        EvidenceCrossCheck.MismatchAsync(evidence, Inputs(), EnvironmentReader.Of(new Dictionary<string, string>()), TestContext.Current.CancellationToken);
}
