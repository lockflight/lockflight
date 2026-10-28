using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReproductionRepairerTests
{
    private static readonly ReproductionRepairRequest Request = new(
        new ReviewFinding(FindingSeverity.Major, "src/Pricing.cs", 3, null, "Discount ignored", "x", "[Fact]\npublic void Applies() => Pricing.Totl();", FindingOutcome.Unverified),
        "Finding1.cs(1,30): CS0117: 'Pricing' does not contain a definition for 'Totl'",
        new TestProjectProfile("Shop.Tests", "tests/Shop.Tests/Shop.Tests.csproj", "net10.0", "xUnit v3", [], [], ["using Xunit;"]),
        "public static class Pricing {\n  public static decimal Total(decimal price) => …;\n}",
        null);

    [Fact]
    public async Task EachRepairGetsWhatIsLeftOfTheBudgetAndAddsItsCost()
    {
        List<decimal> caps = [];
        ReproductionRepairer repairer = new((_, cap, _) => { caps.Add(cap); return Task.FromResult(Reply(0.40m)); }, 1.00m);

        ReproductionRepair first = await repairer.RepairAsync(Request, TestContext.Current.CancellationToken);
        await repairer.RepairAsync(Request, TestContext.Current.CancellationToken);

        Assert.Equal([1.00m, 0.60m], caps);
        Assert.Equal(0.80m, repairer.CostUsd);
        Assert.Equal(2, repairer.Calls);
        Assert.Contains("Pricing.Total(100m)", first.Reproduction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoRepairIsAskedOnceTheBudgetIsSpent()
    {
        int calls = 0;
        ReproductionRepairer repairer = new((_, _, _) => { calls++; return Task.FromResult(Reply(0.10m)); }, 0.04m);

        ReproductionRepair repair = await repairer.RepairAsync(Request, TestContext.Current.CancellationToken);

        Assert.False(repair.Asked);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AFixRepairSharesTheBudgetWithTheReproductionRepairs()
    {
        List<decimal> caps = [];
        ReproductionRepairer repairer = new((_, cap, _) => { caps.Add(cap); return Task.FromResult(Reply(0.40m) with { Findings = [Request.Finding with { Fix = "--- a/src/Pricing.cs\n+++ b/src/Pricing.cs\n" }] }); }, 1.00m);

        await repairer.RepairAsync(Request, TestContext.Current.CancellationToken);
        FixRepair repair = await repairer.RepairFixAsync(new FixRepairRequest(Request.Finding, FixCheckResult.DidNotApply, "hunk 1 does not match"), TestContext.Current.CancellationToken);

        Assert.Equal([1.00m, 0.60m], caps);
        Assert.True(repair.Asked);
        Assert.StartsWith("--- a/src/Pricing.cs", repair.Fix, StringComparison.Ordinal);
        Assert.Equal(2, repairer.Calls);
    }

    [Fact]
    public void TheRepairPromptCarriesTheErrorsTheProjectAndTheSignatures()
    {
        string prompt = ReviewPrompt.Repair(Request);

        Assert.Contains("did not compile in test project\n`tests/Shop.Tests/Shop.Tests.csproj`", prompt.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("'Pricing' does not contain a definition for 'Totl'", prompt, StringComparison.Ordinal);
        Assert.Contains("public static decimal Total(decimal price) => …;", prompt, StringComparison.Ordinal);
        Assert.Contains("namespace `LockflightReproductions`", prompt, StringComparison.Ordinal);
        Assert.Contains("public void Applies() => Pricing.Totl();", prompt, StringComparison.Ordinal);
    }

    private static ReviewOutcome Reply(decimal cost) =>
        new("request_changes", "Repaired.", [Request.Finding with { Reproduction = "[Fact]\npublic void Applies() => Assert.Equal(90m, Pricing.Total(100m));" }], cost, 1000, null);
}
