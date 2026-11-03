using System.Text.Json;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewReproductionFixAndEscalationTests : ReviewReproductionScenario
{
    [Fact]
    public async Task AConfirmedFindingWhoseFixHoldsStillFailsAndIsFixChecked()
    {
        await RepositoryAsync();
        claude.Answers(WithFix(Repaired, SubtractTheDiscount));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("held", finding.GetProperty("fixCheck").GetString());
        Assert.True(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Contains("fix-checked", output, StringComparison.Ordinal);
        Assert.Contains("=> price;", File.ReadAllText(Path.Combine(root.FullName, "src", "Shop", "PriceCalculator.cs")), StringComparison.Ordinal);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()));
        Assert.Equal(1, evidence.RootElement.GetProperty("fixChecks").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task AFixThatStillDoesNotApplyAfterItsRepairLeavesTheFindingConfirmedButNotFixChecked()
    {
        await RepositoryAsync();
        string stale = SubtractTheDiscount.Replace("discount) => price;", "discount) => price * 1;", StringComparison.Ordinal);
        claude.Answers(WithFix(Repaired, stale)).Answers(WithFix(Repaired, stale));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("did-not-apply", finding.GetProperty("fixCheck").GetString());
        Assert.False(finding.GetProperty("fixChecked").GetBoolean());
        Assert.Contains("not fix-checked: the reviewer's fix did not apply", output, StringComparison.Ordinal);
        Assert.Equal(2, finding.GetProperty("fixAttempts").GetInt32());
        Assert.Contains("src/Shop/PriceCalculator.cs", finding.GetProperty("fixCheckErrors").GetString(), StringComparison.Ordinal);
        Assert.Equal(2, claude.Calls.Count(arguments => arguments.Contains("-p")));
        Assert.Contains("did not apply", claude.Prompts[1], StringComparison.Ordinal);
        Assert.Contains("price * 1", claude.Prompts[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingThatBreaksAnAnchoredMustAndReproducesIsEscalatedAndFixChecked()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(WithFix(MinorMust(), SubtractTheDiscount));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode == ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("major", finding.GetProperty("severity").GetString());
        Assert.Equal("minor", finding.GetProperty("originalSeverity").GetString());
        Assert.Equal("escalated: anchored MUST, reproduced", finding.GetProperty("escalation").GetString());
        Assert.Equal("confirmed", finding.GetProperty("outcome").GetString());
        Assert.Equal("open", finding.GetProperty("state").GetString());
        Assert.Equal("anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal("held", finding.GetProperty("fixCheck").GetString());
        Assert.Contains("escalated: anchored MUST, reproduced (the reviewer rated it minor)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseReproductionPassesStaysMinor()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust("Assert.Equal(100m, PriceCalculator.Total(100m, 10m))"));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("originalSeverity").ValueKind);
        Assert.Equal("reported", finding.GetProperty("outcome").GetString());
        Assert.Equal("passed", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("state").ValueKind);
        Assert.Contains("the reproduction passes on the current code", finding.GetProperty("escalation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseReproductionCannotBeRepairedWithinTheBudgetStaysMinorAndSaysSo()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust("Assert.Equal(90m, PriceCalculator.Totl(100m, 10m))"));

        (ExitCode exitCode, string output) = await RunAsync("review", "--budget", "0.25");

        Assert.True(exitCode != ExitCode.Fail, output);
        Assert.Single(claude.Calls, arguments => arguments.Contains("-p"));
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("did-not-compile", finding.GetProperty("reproductionResult").GetString());
        Assert.Contains("the run's budget was spent before it could be repaired", finding.GetProperty("escalation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMinorFindingWhoseQuoteIsNotInTheSpecIsNotEscalatedOrRun()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust().Replace("MUST apply the discount", "MUST round the discount", StringComparison.Ordinal));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("not-anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("escalation").ValueKind);
        Assert.Equal("none", finding.GetProperty("reproductionResult").GetString());
        Assert.False(Directory.Exists(Path.Combine(root.FullName, "tests", "Shop.Tests", "bin", "lockflight-review")));
    }

    [Fact]
    public async Task AMinorFindingWhoseAnchoredQuoteHasNoMustIsNotEscalated()
    {
        await RepositoryAsync("- **FR-001**: The total applies the discount.\n");
        claude.Answers(MinorMust().Replace("The total MUST apply the discount.", "The total applies the discount.", StringComparison.Ordinal));

        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single());
        Assert.Equal("minor", finding.GetProperty("severity").GetString());
        Assert.Equal("anchored", finding.GetProperty("specAnchor").GetString());
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("escalation").ValueKind);
        Assert.Equal("none", finding.GetProperty("reproductionResult").GetString());
    }

    [Fact]
    public async Task AnEscalatedFindingCanBeDismissedAndStaysDismissedWhenRaisedAgain()
    {
        await RepositoryAsync(MustSpec);
        claude.Answers(MinorMust()).Answers(MinorMust());
        await RunAsync();
        string id = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Single()).GetProperty("id").GetString()!;

        (ExitCode dismissed, string dismissOutput) = await RunAsync("review", "dismiss", id, "--reason", "The discount is applied upstream");
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode exitCode, string output) = await RunAsync();

        Assert.True(dismissed == ExitCode.Pass, dismissOutput);
        Assert.True(exitCode != ExitCode.Fail, output);
        JsonElement finding = SingleFinding(Directory.GetFiles(ReviewsDirectory(), "*.json").Order(StringComparer.Ordinal).Last());
        Assert.Equal("dismissed", finding.GetProperty("state").GetString());
    }
}
