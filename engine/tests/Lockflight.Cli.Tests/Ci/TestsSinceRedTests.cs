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
