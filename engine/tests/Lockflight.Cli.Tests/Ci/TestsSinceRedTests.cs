using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// Tests since red on real builds: a test changed after its red counts only when the head's version, with the head's whole test
/// side, still fails on the red commit's code.
/// </summary>
[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedTests : IAsyncLifetime
{
    private const string Zero = "    public decimal Shipping(decimal kilograms) => 0m;";
    private const string Stub = "    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();";
    private const string Implementation = "    public decimal Shipping(decimal kilograms) => kilograms * 2m;";
    private const string Test = """
            [Fact]
            public void ShippingIsTwiceTheWeight() => Expect.Same(4m, new Price().Shipping(2m));
        """;

    private readonly DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-since-");
    private GitHistoryFixture repository = null!;
    private string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(string.Empty, string.Empty).Write("tests/Shop.Tests/Expect.cs", Helper("Assert.Equal(expected, actual);"))
            .CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-shipping", cancellationToken);
        await repository.WriteCode(Zero, Test).CommitAsync("test: red for US1", cancellationToken, null, "Lockflight-Red: 1");
        await repository.WriteCode(Implementation, Test).CommitAsync("feat: US1", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        TempDirectories.DeleteRecursively(evidence);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AnUnchangedTestPasses()
    {
        JsonNode document = await JudgeAsync(await repository.HeadAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("unchanged", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    [Fact]
    public async Task ATestWeakenedAfterGreenIsRejected()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Implementation, Test.Replace("new Price().Shipping(2m)", "4m", StringComparison.Ordinal)).CommitAsync("test: simpler", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Contains("no longer checks the behaviour it went red for", Why(document), StringComparison.Ordinal);
        Assert.Equal("amend-rejected", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnHonestAmendThatStillFailsOnTheRedCodeIsAccepted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Implementation, Test.Replace("Same(4m, new Price().Shipping(2m))", "Same(6m, new Price().Shipping(3m))", StringComparison.Ordinal))
            .CommitAsync("test: another weight", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("amend-accepted", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
        Assert.Equal("RED_ASSERT", document["cycles"]![0]!["tests"]![0]!["amend"]!["red"]!.GetValue<string>());
    }

    [Fact]
    public async Task AHelperGuttedInAnotherFileIsPartOfTheRecheck()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.Write("tests/Shop.Tests/Expect.cs", Helper("_ = expected; _ = actual;")).CommitAsync("refactor: helper", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Contains("GREEN_BEFORE", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAmendInAFileALaterCycleNeedsNewApisInCannotBeRecheckedAndIsNotEarned()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Tax = "\n    public decimal Tax(decimal net) => net / 5m;";
        const string TaxTest = "\n    [Fact]\n    public void TaxIsAFifth() => Expect.Same(20m, new Price().Tax(100m));";
        string changed = Test.Replace("Same(4m, new Price().Shipping(2m))", "Same(6m, new Price().Shipping(3m))", StringComparison.Ordinal);
        await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => 0m;", changed + TaxTest).CommitAsync("test: red for US2", cancellationToken, null, "Lockflight-Red: 2");
        string head = await repository.WriteCode(Implementation + Tax, changed + TaxTest).CommitAsync("feat: US2", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Verdict(document));
        Assert.Contains("do not build on", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAssemblyFixtureAddedAfterRedIsPartOfTheRecheck()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Seam = "    public static decimal? Override { get; set; }\n";
        await repository.WriteCode(Seam + "    public decimal Shipping(decimal kilograms) => Override ?? throw new NotImplementedException();", Test)
            .CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Seam + "    public decimal Shipping(decimal kilograms) => Override ?? 0m;", Test)
            .Write("tests/Shop.Tests/Patch.cs", "[assembly: AssemblyFixture(typeof(Shop.Tests.Patch))]\n\nnamespace Shop.Tests;\n\npublic sealed class Patch\n{\n    public Patch() => Shop.Price.Override = 4m;\n}\n")
            .CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Contains("GREEN_BEFORE", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFrameworkFreeSupportProjectChangedAfterRedIsCompared()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string supportTest = Test.Replace("Expect.Same", "Shop.Support.Check.Same", StringComparison.Ordinal);
        await repository.Write("tests/Shop.Support/Shop.Support.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n")
            .Write("tests/Shop.Support/Check.cs", Support("if (expected != actual) throw new System.InvalidOperationException($\"{expected} != {actual}\");"))
            .Write("tests/Shop.Tests/Shop.Tests.csproj", (await File.ReadAllTextAsync(Path.Combine(repository.Root, "tests/Shop.Tests/Shop.Tests.csproj"), cancellationToken))
                .Replace("<ProjectReference Include=\"../../src/Shop/Shop.csproj\" />", "<ProjectReference Include=\"../../src/Shop/Shop.csproj\" />\n    <ProjectReference Include=\"../Shop.Support/Shop.Support.csproj\" />", StringComparison.Ordinal))
            .WriteCode(Stub, supportTest).CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.Write("tests/Shop.Support/Check.cs", Support("_ = expected; _ = actual;"))
            .WriteCode("    public decimal Shipping(decimal kilograms) => 0m;", supportTest).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Verdict(document));
        Assert.Equal("amend-changed-expectation", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    [Fact]
    public async Task ATestWeakenedToWhatTheStubFailsFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.WriteCode(Stub, Test).CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Zero, Test.Replace("Expect.Same(4m, new Price().Shipping(2m))", "Assert.True(new Price().Shipping(2m) >= 0m)", StringComparison.Ordinal))
            .CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Equal("amend-weakened", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
        Assert.Contains("1 rows of 1 assertion calls where the red commit's version has 1 rows of 2", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExpectationChangedToWhatTheCodeDoesIsNotEarned()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.WriteCode(Stub, Test).CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode(Zero, Test.Replace("Same(4m,", "Same(0m,", StringComparison.Ordinal)).CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Verdict(document));
        Assert.Equal("amend-changed-expectation", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
        Assert.Contains("the red commit's version fails on the head's code", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADataRowDroppedAfterRedFails()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string Rows = """
                [Theory]
                [InlineData(2, 4)]
                [InlineData(3, 6)]
                public void ShippingIsTwiceTheWeight(int kilograms, int expected) => Expect.Same(expected, new Price().Shipping(kilograms));
            """;
        await repository.WriteCode(Stub, Rows).CommitAsync("test: red for US1 again", cancellationToken, null, "Lockflight-Red: 1");
        string head = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => 4m;", Rows.Replace("    [InlineData(3, 6)]\n", string.Empty, StringComparison.Ordinal))
            .CommitAsync("feat: US1", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("FAIL", Verdict(document));
        Assert.Equal("amend-weakened", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
        Assert.Contains("1 rows where the red commit's version has 2", Why(document), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALaterCyclesTestFileIsLeftOutOfTheRecheck()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string changed = Test.Replace("Same(4m, new Price().Shipping(2m))", "Same(6m, new Price().Shipping(3m))", StringComparison.Ordinal);
        const string TaxTests = "namespace Shop.Tests;\n\npublic sealed class TaxTests\n{\n    [Fact]\n    public void TaxIsAFifth() => Expect.Same(20m, new Shop.Price().Tax(100m));\n}\n";
        await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => 0m;", changed).Write("tests/Shop.Tests/TaxTests.cs", TaxTests)
            .CommitAsync("test: red for US2", cancellationToken, null, "Lockflight-Red: 2");
        string head = await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => net / 5m;", changed).CommitAsync("feat: US2", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("amend-accepted", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    [Fact]
    public async Task AHelperFileALaterCycleAddsLeavesEarlierTestsUnchanged()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string TaxCheck = "namespace Shop.Tests;\n\ninternal static class TaxCheck\n{\n    public static void Fifth(decimal net) => Expect.Same(net / 5m, new Shop.Price().Tax(net));\n}\n";
        const string TaxTests = "namespace Shop.Tests;\n\npublic sealed class TaxTests\n{\n    [Fact]\n    public void TaxIsAFifth() => TaxCheck.Fifth(100m);\n}\n";
        await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => 0m;", Test).Write("tests/Shop.Tests/TaxCheck.cs", TaxCheck).Write("tests/Shop.Tests/TaxTests.cs", TaxTests)
            .CommitAsync("test: red for US2", cancellationToken, null, "Lockflight-Red: 2");
        string head = await repository.WriteCode(Implementation + "\n    public decimal Tax(decimal net) => net / 5m;", Test).CommitAsync("feat: US2", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("unchanged", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    private static string Support(string body) => $$"""
        namespace Shop.Support;

        public static class Check
        {
            public static void Same(decimal expected, decimal actual)
            {
                {{body}}
            }
        }

        """;

    [Fact]
    public async Task HelperChangesAndTestsMergedFromTheTargetAfterARedAreNoAmend()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await repository.CheckoutAsync("main", cancellationToken);
        main = await repository.Write("tests/Shop.Tests/Expect.cs", Helper("Assert.Equal(expected, actual);") + "\ninternal static class Near\n{\n    public static bool Enough(decimal left, decimal right) => Math.Abs(left - right) < 0.01m;\n}\n")
            .Write("src/Shop/Discount.cs", "namespace Shop;\n\npublic static class Discount\n{\n    public static decimal Off(decimal value) => value / 10m;\n}\n")
            .Write("tests/Shop.Tests/DiscountTests.cs", "namespace Shop.Tests;\n\npublic sealed class DiscountTests\n{\n    [Fact]\n    public void TenPercent() => Assert.True(Near.Enough(1m, Discount.Off(10m)));\n}\n")
            .CommitAsync("feat: discount", cancellationToken);
        await repository.CheckoutAsync("001-shipping", cancellationToken);
        string head = await repository.MergeAsync("main", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("unchanged", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

    [Fact]
    public async Task ACycleTestRenamedAfterItsRedIsNotEarnedWithTheWayOut()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string head = await repository.WriteCode(Implementation, Test.Replace("ShippingIsTwiceTheWeight", "ShippingDoublesTheWeight", StringComparison.Ordinal))
            .CommitAsync("test: rename", cancellationToken);

        JsonNode document = await JudgeAsync(head);

        Assert.Equal("NOT-EARNED", Verdict(document));
        Assert.Contains("keep the name", Why(document), StringComparison.Ordinal);
        JsonNode green = document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "green")!;
        Assert.Equal("NOT-EARNED", green["verdict"]!.GetValue<string>());
        Assert.Contains("ShippingIsTwiceTheWeight", green["why"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    private static string Helper(string body) => $$"""
        namespace Shop.Tests;

        internal static class Expect
        {
            public static void Same(decimal expected, decimal actual)
            {
                {{body}}
            }
        }

        """;

    private async Task<JsonNode> JudgeAsync(string head)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence.FullName });
        string path = Path.Combine(evidence.FullName, "evidence.json");
        Assert.True(File.Exists(path), $"exit {(int)exitCode}: {output}\n{error}");
        return JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
    }

    private static JsonNode SinceRed(JsonNode document) => document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "tests.sinceRed")!;

    private static string Verdict(JsonNode document) => SinceRed(document)["verdict"]!.GetValue<string>();

    private static string Why(JsonNode document) => SinceRed(document)["why"]!.GetValue<string>();
}
