using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A structural-only change on a real build: a refactor whose spec is all <c>[structural]</c>, changed under CODEOWNERS, passes
/// without a red commit when its new structural tests pass on the fork point and at head, it adds no other test, and the base's
/// version of every existing test it changed passes on its code. A behaviour change does not pass, however it is dressed.
/// </summary>
[Trait("Category", "CiIntegration")]
public sealed class StructuralChangeJudgeTests : IAsyncLifetime
{
    private const string Branch = "012-pricing";
    private const string Taxed = "    public decimal Total(decimal net) => net * 1.2m;";
    private const string Refactored = "    private const decimal Tax = 1.2m;\n\n    public decimal Total(decimal net) => net * Tax;";
    private const string RaisedTax = "    public decimal Total(decimal net) => net * 1.25m;";
    private const string BaseTest = "    [Fact]\n    public void TotalAddsTax() => Expect.Total(12m, new Price().Total(10m));";
    private const string Helper = "namespace Shop.Tests;\n\ninternal static class Expect\n{\n    public static void Total(decimal expected, decimal actual) => Assert.Equal(expected, actual);\n}\n";
    private const string StructuralTestsFile = "tests/Shop.Tests/PricingStructureTests.cs";
    private const string Spec = "# Feature Specification: Pricing\n\n## Requirements *(mandatory)*\n\n- **FR-010** [structural]: Order totals MUST stay as they are after the tax rate is named\n";

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-structural-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(Taxed, BaseTest).Write("tests/Shop.Tests/Expect.cs", Helper).Write(".github/CODEOWNERS", "specs/** @team\n")
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync(Branch, cancellationToken);
        repository.Write("specs/012-pricing/spec.md", Spec);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ARefactorWithAStructuralTestPassesWithoutARedCommit()
    {
        string head = await repository.WriteCode(Refactored, BaseTest).Write(StructuralTestsFile, StructuralTest("24m", "20m")).CommitAsync("refactor: name the tax rate", TestContext.Current.CancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", document["verdict"]!.GetValue<string>());
        Assert.Equal("N/A", Check(document, "red")["verdict"]!.GetValue<string>());
        Assert.Equal(JudgeChecks.StructuralLaneWhy, Check(document, "red")["why"]!.GetValue<string>());
        Assert.Equal("PASS", Check(document, "trace")["verdict"]!.GetValue<string>());
        Assert.Equal("required", Check(document, "tests.existingChanged")["requirement"]!.GetValue<string>());
        JsonNode structural = document["structuralChange"]!;
        Assert.True(structural["eligible"]!.GetValue<bool>());
        Assert.Equal("codeowners", structural["route"]!.GetValue<string>());
        Assert.Equal("preserved", structural["tests"]![0]!["outcome"]!.GetValue<string>());
        Assert.Equal("not-measured", structural["coverage"]!.GetValue<string>());
    }

    [Fact]
    public async Task ABehaviourChangeItsStructuralTestPinsFailsTrace()
    {
        string head = await repository.WriteCode(RaisedTax, BaseTest.Replace("12m", "12.5m", StringComparison.Ordinal))
            .Write(StructuralTestsFile, StructuralTest("25m", "20m")).CommitAsync("refactor: tax", TestContext.Current.CancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.NotEqual("PASS", document["verdict"]!.GetValue<string>());
        Assert.Equal("FAIL", Check(document, "trace")["verdict"]!.GetValue<string>());
        Assert.Contains("behaviour changed", Check(document, "trace")["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewPlainTestInAStructuralChangeIsNotEarned()
    {
        string plain = BaseTest + "\n\n    [Fact]\n    public void ZeroIsUntaxed() => Assert.Equal(0m, new Price().Total(0m));";
        string head = await repository.WriteCode(Refactored, plain).Write(StructuralTestsFile, StructuralTest("24m", "20m")).CommitAsync("refactor: name the tax rate", TestContext.Current.CancellationToken);

        JsonNode trace = Check(await JudgeAsync(head), "trace");

        Assert.Equal("NOT-EARNED", trace["verdict"]!.GetValue<string>());
        Assert.Equal("STRUCTURAL_CHANGE_NEW_TEST", trace["reason"]!.GetValue<string>());
        Assert.Contains("PriceTests.ZeroIsUntaxed", trace["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingTestWeakenedToAcceptTheChangeFails()
    {
        string weakened = "    [Fact]\n    public void TotalAddsTax() => Assert.True(new Price().Total(10m) > 10m);";
        string head = await repository.WriteCode(RaisedTax, weakened).Write(StructuralTestsFile, StructuralTest("0m", "0m")).CommitAsync("refactor: tax", TestContext.Current.CancellationToken);

        JsonNode existing = Check(await JudgeAsync(head), "tests.existingChanged");

        Assert.Equal("FAIL", existing["verdict"]!.GetValue<string>());
        Assert.Equal("EXISTING_TEST_OLD_VERSION_FAILS", existing["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task ASharedHelperWeakenedToAcceptTheChangeFails()
    {
        string head = await repository.WriteCode(RaisedTax, BaseTest)
            .Write("tests/Shop.Tests/Expect.cs", Helper.Replace("Assert.Equal(expected, actual)", "Assert.True(actual > 0m)", StringComparison.Ordinal))
            .Write(StructuralTestsFile, StructuralTest("0m", "0m")).CommitAsync("refactor: tax", TestContext.Current.CancellationToken);

        JsonNode existing = Check(await JudgeAsync(head), "tests.existingChanged");

        Assert.Equal("FAIL", existing["verdict"]!.GetValue<string>());
        Assert.Contains("TotalAddsTax", existing["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingTestChangedButKeptPasses()
    {
        string head = await repository.WriteCode(Refactored, BaseTest.Replace("12m", "12.0m", StringComparison.Ordinal))
            .Write(StructuralTestsFile, StructuralTest("24m", "20m")).CommitAsync("refactor: name the tax rate", TestContext.Current.CancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Check(document, "tests.existingChanged")["verdict"]!.GetValue<string>());
        Assert.Contains("the old version of each passes on the head's code", Check(document, "tests.existingChanged")["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(1, document["structuralChange"]!["existingChanged"]!["kept"]!.GetValue<int>());
        Assert.Equal("PASS", document["verdict"]!.GetValue<string>());
    }

    [Fact]
    public async Task ARenamedApiWhoseOldTestCannotBuildIsNotEarned()
    {
        string renamed = "    public decimal Gross(decimal net) => net * 1.2m;";
        string head = await repository.WriteCode(renamed, BaseTest.Replace(".Total(10m)", ".Gross(10m)", StringComparison.Ordinal))
            .Write(StructuralTestsFile, StructuralTest("24m", "20m").Replace(".Total(", ".Gross(", StringComparison.Ordinal))
            .CommitAsync("refactor: rename", TestContext.Current.CancellationToken);

        JsonNode existing = Check(await JudgeAsync(head), "tests.existingChanged");

        Assert.Equal("NOT-EARNED", existing["verdict"]!.GetValue<string>());
        Assert.Equal("EXISTING_TEST_OLD_VERSION_UNCHECKABLE", existing["reason"]!.GetValue<string>());
    }

    private static string StructuralTest(string expected, string net) => $$"""
        using Shop;

        namespace Shop.Tests;

        public sealed class PricingStructureTests
        {
            [Fact]
            [Trait("Requirement", "FR-010")]
            public void TotalsStayAsTheyWere() => Assert.Equal({{expected}}, new Price().Total({{net}}));
        }

        """;

    private static JsonNode Check(JsonNode document, string id) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == id)!;

    private async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--branch", Branch, "--evidence", evidence.FullName], string.Empty, repository.Root,
            new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }
}
