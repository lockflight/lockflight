using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The report re-plans the run from git and refuses evidence that disagrees with it: a rewritten <c>evidence.json</c> that turns
/// red, tests since red or trace off, or names another policy, class or red commits, is an ERROR, not a PASS.
/// </summary>
public sealed class EvidenceCrossCheckTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;
    private string baseCommit = string.Empty;
    private string head = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        baseCommit = await repository.Write("lockflight.yaml", "version: 1\n").Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n")
            .Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n}\n")
            .Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"3.0.0\" />\n  </ItemGroup>\n</Project>\n")
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("012-prices", cancellationToken);
        head = await repository.Write("src/Shop/Price.cs", "namespace Shop;\n\npublic sealed class Price\n{\n    public decimal Shipping() => 2m;\n}\n")
            .CommitAsync("feat: shipping", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task EvidenceThatAgreesWithThePlanIsAccepted() =>
        Assert.Null(await MismatchAsync(await EvidenceAsync()));

    [Theory]
    [InlineData("red")]
    [InlineData("tests.sinceRed")]
    [InlineData("tests.hostTampering")]
    public async Task EvidenceThatTurnsARequiredCheckOffIsRefused(string id)
    {
        JsonObject evidence = await EvidenceAsync();
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!["requirement"] = "off";

        Assert.Contains($"it has {id} off, and the plan makes it required", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("tests.sinceRed")]
    public async Task EvidenceThatPassesACheckWithoutARedCommitIsRefused(string id)
    {
        JsonObject evidence = await EvidenceAsync();
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!["verdict"] = "PASS";

        Assert.Contains($"it has {id} PASS, and git alone makes it NOT-EARNED", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceWithoutTheEngineVersionIsRefused()
    {
        JsonObject evidence = await EvidenceAsync();
        evidence.Remove("engine");

        Assert.Contains("it was written by engine none", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceJudgedWithAnotherPolicyIsRefused()
    {
        JsonObject evidence = await EvidenceAsync();
        evidence["policy"]!["from"] = head;

        Assert.Contains("judged with the policy of", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceWithAnotherClassIsRefused()
    {
        JsonObject evidence = await EvidenceAsync();
        string other = evidence["changeClass"]!["class"]!.GetValue<string>() == "fix" ? "behaviour" : "fix";
        evidence["changeClass"]!["class"] = other;

        Assert.Contains($"its change class is {other}", await MismatchAsync(evidence), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportCommandTurnsADisagreementIntoAnError()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JsonObject evidence = await EvidenceAsync();
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!["requirement"] = "off";
        string evidenceDirectory = Path.Combine(repository.Root, "out", "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, CiEvidence.FileName), evidence.ToJsonString(), cancellationToken);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(
            ["ci", "report", "--evidence", evidenceDirectory, "--head", head, "--base", baseCommit, "--event", "pr", "--platform", "github", "--out", Path.Combine(repository.Root, "out", "verdict")],
            string.Empty, repository.Root);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.StartsWith("ERROR", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceThatCallsItselfMissingIsStillCrossChecked()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        JsonObject evidence = await EvidenceAsync();
        evidence["judge"] = "missing";
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!["requirement"] = "off";
        string evidenceDirectory = Path.Combine(repository.Root, "out", "evidence");
        Directory.CreateDirectory(evidenceDirectory);
        await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, CiEvidence.FileName), evidence.ToJsonString(), cancellationToken);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(
            ["ci", "report", "--evidence", evidenceDirectory, "--head", head, "--base", baseCommit, "--event", "pr", "--platform", "github", "--out", Path.Combine(repository.Root, "out", "verdict")],
            string.Empty, repository.Root);

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.StartsWith("ERROR", output, StringComparison.Ordinal);
    }

    private async Task<JudgePlan> PlanAsync()
    {
        JudgeInputs inputs = new(repository.Root, head, baseCommit, "012-prices", JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: true);
        JudgePlanning planning = await JudgePlanner.PlanAsync(inputs, EnvironmentReader.Of(new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        Assert.True(planning.Plan is not null, planning.Error);
        return planning.Plan;
    }

    /// <summary>Evidence as the judge would write it for this plan: its policy, engine, event, class, red commits and levels, every check passing
    /// unless git alone decides it.</summary>
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
            ["checks"] = new JsonArray([.. plan.Levels.Select(level => (JsonNode)new JsonObject
            {
                ["id"] = CheckIdNames.ToName(level.Key),
                ["context"] = StatusContextNames.ToName(new CheckResult(level.Key, level.Value, CheckVerdict.Pass, "ok").Context),
                ["verdict"] = level.Value == CheckLevel.Off ? "N/A" : CheckVerdictNames.ToName(EvidenceCrossCheck.PlannedVerdict(plan, level.Key) ?? CheckVerdict.Pass),
                ["requirement"] = CheckLevelNames.ToName(level.Value),
                ["why"] = "ok",
            })]),
        };
    }

    private async Task<string?> MismatchAsync(JsonObject evidence)
    {
        JudgeInputs inputs = new(repository.Root, head, baseCommit, "012-prices", JudgePlatform.GitHub, JudgeEvent.PullRequest, Path.Combine(repository.Root, "out"), null, ReviewExpected: true);
        return await EvidenceCrossCheck.MismatchAsync(evidence, inputs, EnvironmentReader.Of(new Dictionary<string, string>()), TestContext.Current.CancellationToken);
    }
}
