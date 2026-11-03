using System.Text.Json.Nodes;

namespace Lockflight.Cli.Tests.Ci;

[Trait("Category", "CiIntegration")]
public sealed class TestsSinceRedAmendTests : TestsSinceRedScenario
{
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
}
