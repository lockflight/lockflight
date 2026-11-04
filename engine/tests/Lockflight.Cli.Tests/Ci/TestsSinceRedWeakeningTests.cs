using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedWeakeningTests : TestsSinceRedScenario
{
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
}
