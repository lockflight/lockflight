using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedLaterCycleTests : TestsSinceRedScenario
{
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
        Assert.Contains("'redgreen red' warns", Why(document), StringComparison.Ordinal);
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
}
