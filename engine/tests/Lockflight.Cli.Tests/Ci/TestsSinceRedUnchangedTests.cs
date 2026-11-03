using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedUnchangedTests : TestsSinceRedScenario
{
    [Fact]
    public async Task AnUnchangedTestPasses()
    {
        JsonNode document = await JudgeAsync(await repository.HeadAsync(TestContext.Current.CancellationToken));

        Assert.Equal("PASS", Verdict(document));
        Assert.Equal("unchanged", document["cycles"]![0]!["tests"]![0]!["sinceRed"]!.GetValue<string>());
    }

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
}
