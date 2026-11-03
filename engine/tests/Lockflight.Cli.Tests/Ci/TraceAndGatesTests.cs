using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>Trace from the head's spec and the pr gates at head, on a real build of a pull request with one story.</summary>
[Trait("Category", "CiIntegration")]
public sealed class TraceAndGatesTests : IAsyncLifetime
{
    private const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string Test = "    [Fact]\n    [Trait(\"Requirement\", \"FR-001\")]\n    public void ShippingIsTwiceTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));";

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-trace-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(string.Empty, string.Empty).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task EveryRequirementTracedToATestThatWentRedThenGreenPassesAndTheGatesRunAtHead()
    {
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n");

        JsonNode document = await JudgeAsync(head, "001-shipping");

        Assert.Equal("PASS", Check(document, "trace")["verdict"]!.GetValue<string>());
        Assert.Equal("required", Check(document, "trace")["requirement"]!.GetValue<string>());
        Assert.Equal("001-shipping", document["feature"]!["id"]!.GetValue<string>());
        Assert.Equal("branch", document["feature"]!["source"]!.GetValue<string>());
        Assert.StartsWith("lockflight.result/", document["gates"]!["schema"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("build PASS", Check(document, "gates")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequirementNoRedTestNamesFailsTrace()
    {
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n- **FR-002**: Shipping MUST be free above 100 kg\n");

        JsonNode trace = Check(await JudgeAsync(head, "001-shipping"), "trace");

        Assert.Equal("FAIL", trace["verdict"]!.GetValue<string>());
        Assert.Contains("FR-002", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABranchNamedSoNoSpecIsFoundDoesNotDodgeTrace()
    {
        repository.Write("specs/002-other/spec.md", "# Other\n");
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n");

        JsonNode trace = Check(await JudgeAsync(head, "quick-fix"), "trace");

        Assert.Equal("NOT-EARNED", trace["verdict"]!.GetValue<string>());
        Assert.Equal("required", trace["requirement"]!.GetValue<string>());
    }

    [Fact]
    public async Task ASpecOnTheBaseIsTracedOnlyForTheRequirementsThePullRequestAddsOrItsRedTestsName()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write("specs/001-shipping/spec.md", "# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n- **FR-001**: Shipping MUST cost twice the weight\n- **FR-009**: Earlier story, traced when it merged\n")
            .CommitAsync("spec", cancellationToken);
        await repository.CheckoutAsync("001-shipping", cancellationToken);
        await repository.MergeAsync("main", cancellationToken);
        await repository.WriteCode(Stub, Test).CommitAsync("test(001-shipping): red for US1", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Implementation, Test).CommitAsync("feat(001-shipping): US1", cancellationToken);

        JsonNode trace = Check(await JudgeAsync(head, "001-shipping"), "trace");

        Assert.Equal("PASS", trace["verdict"]!.GetValue<string>());
        Assert.StartsWith("1/1 requirements", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequirementVerifiedOutsideDotNetIsExcludedAndTheRestTraced()
    {
        repository.Write("web/card.spec.ts", "it('FR-002 shows the shipping card', () => {});\n");
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n- **FR-002** [outside .NET: web/card.spec.ts]: The card MUST show the shipping\n");

        JsonNode document = await JudgeAsync(head, "001-shipping");

        JsonNode trace = Check(document, "trace");
        Assert.Equal("PASS", trace["verdict"]!.GetValue<string>());
        Assert.Contains("1 verified outside .NET", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, document["trace"]!["excluded"]!.GetValue<int>());
        JsonNode row = document["trace"]!["rows"]!.AsArray().Single(entry => entry!["requirement"]!.GetValue<string>() == "FR-002")!;
        Assert.Equal("excluded", row["status"]!.GetValue<string>());
        Assert.Equal("web/card.spec.ts", row["outside"]![0]!["path"]!.GetValue<string>());
        Assert.Equal("names", row["outside"]![0]!["state"]!.GetValue<string>());
        Assert.Equal("added in this pull request", row["outside"]![0]!["change"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnOutsideMarkerWhoseFileDoesNotNameTheRequirementFailsTrace()
    {
        repository.Write("web/card.spec.ts", "it('shows the shipping card', () => {});\n");
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n- **FR-002** [outside .NET: web/card.spec.ts]: The card MUST show the shipping\n");

        JsonNode trace = Check(await JudgeAsync(head, "001-shipping"), "trace");

        Assert.Equal("FAIL", trace["verdict"]!.GetValue<string>());
        Assert.Contains("FR-002", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APullRequestWhoseEveryRequirementIsVerifiedOutsideDotNetIsNotApplicableForTrace()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        // A production change, so trace applies; no .NET test names the requirement.
        repository.WriteCode(Implementation, string.Empty);
        string head = await repository.Write("web/card.spec.ts", "it('FR-001 shows the shipping card', () => {});\n")
            .Write("specs/001-shipping/spec.md", "# Feature Specification: Shipping\n\n## Requirements\n\n- **FR-001** [outside .NET: web/card.spec.ts]: The card MUST show the shipping\n")
            .CommitAsync("feat(001-shipping): card", cancellationToken);

        JsonNode document = await JudgeAsync(head, "001-shipping");

        JsonNode trace = Check(document, "trace");
        Assert.Equal("N/A", trace["verdict"]!.GetValue<string>());
        Assert.Contains("verified outside .NET", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("excluded", document["trace"]!["rows"]![0]!["status"]!.GetValue<string>());
        Assert.Equal("required", trace["requirement"]!.GetValue<string>());
        Assert.NotEqual("PASS", document["verdict"]!.GetValue<string>());
    }

    [Fact]
    public async Task AMarkerAddedToARequirementOfTheBaseIsCalledOut()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write("specs/001-shipping/spec.md", "# Feature Specification: Shipping\n\n## Requirements\n\n- **FR-001**: Shipping MUST cost twice the weight\n- **FR-009**: Earlier story, traced when it merged\n")
            .CommitAsync("spec", cancellationToken);
        await repository.CheckoutAsync("001-shipping", cancellationToken);
        await repository.MergeAsync("main", cancellationToken);
        repository.Write("web/old.spec.ts", "it('FR-009 still works', () => {});\n");
        string head = await StoryAsync("- **FR-001**: Shipping MUST cost twice the weight\n- **FR-009** [outside .NET: web/old.spec.ts]: Earlier story, traced when it merged\n");

        JsonNode document = await JudgeAsync(head, "001-shipping");

        Assert.Equal(["FR-009: [outside .NET: web/old.spec.ts] added"], document["trace"]!["markerChanges"]!.AsArray().Select(change => change!.GetValue<string>()));
        Assert.Equal("excluded", document["trace"]!["rows"]!.AsArray().Single(entry => entry!["requirement"]!.GetValue<string>() == "FR-009")!["status"]!.GetValue<string>());
    }

    private async Task<string> StoryAsync(string requirements)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository.Write("specs/001-shipping/spec.md", $"# Feature Specification: Shipping\n\n## Requirements *(mandatory)*\n\n{requirements}");
        await repository.WriteCode(Stub, Test).CommitAsync("test(001-shipping): red for US1", cancellationToken, null, "Lockflight-Red: 1");
        return await repository.WriteCode(Implementation, Test).CommitAsync("feat(001-shipping): US1", cancellationToken);
    }

    private async Task<JsonNode> JudgeAsync(string head, string branch)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--branch", branch, "--evidence", evidence.FullName], string.Empty, repository.Root,
            new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName, ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? string.Empty, ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? string.Empty });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }

    private static JsonNode Check(JsonNode document, string id) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!;
}
