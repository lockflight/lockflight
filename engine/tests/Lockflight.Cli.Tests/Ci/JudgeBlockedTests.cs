using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgeBlockedTests
{
    private static readonly string Head = new('a', 40);

    /// <summary>The checks that are off in the evidence of a required review no key ran: a report joins only complete evidence.</summary>
    private static readonly string[] OffChecks = ["red", "tests.sinceRed", "tests.existingChanged", "tests.hostTampering", "trace", "approvals", "routing", "policy"];

    [Fact]
    public void ARunOutOfTimeWaitsForAMaintainerToRaiseTheLimit()
    {
        JudgeBudget budget = new(TimeSpan.Zero, TimeProvider.System);
        budget.TryStart("gates");

        BlockedReport report = JudgeBlocked.For(Run(new CheckResult(CheckId.Gates, CheckLevel.Required, CheckVerdict.NotEarned, budget.Why(2)), budget))!;

        Assert.Equal(BlockedReasonCode.TimeExhausted, report.Code);
        Assert.Contains("not run: gates", report.Summary, StringComparison.Ordinal);
        Assert.Equal((BlockedWho.Maintainer, BlockedAction.RaiseLimit), (report.Needs[0].Who, report.Needs[0].Action));
        Assert.Equal(["lockflight.yaml"], report.Needs[0].Paths);
    }

    [Fact]
    public void ARunOutOfTimeWithSomethingTheAgentCanFixWaitsForNoPerson()
    {
        JudgeBudget budget = new(TimeSpan.Zero, TimeProvider.System);
        budget.TryStart("gates");
        JudgeRun run = Run(new CheckResult(CheckId.Gates, CheckLevel.Required, CheckVerdict.NotEarned, budget.Why(2)), budget);

        Assert.Null(JudgeBlocked.For(run with { Checks = [.. run.Checks, new CheckResult(CheckId.Red, CheckLevel.Required, CheckVerdict.NotEarned, "no red commit")] }));
    }

    [Fact]
    public void ANotEarnedTheAgentCanFixWaitsForNoPerson() =>
        Assert.Null(JudgeBlocked.For(Run(new CheckResult(CheckId.Red, CheckLevel.Required, CheckVerdict.NotEarned, "no red commit"), budget: null)));

    [Fact]
    public void APassingRunWaitsForNoPerson() =>
        Assert.Null(JudgeBlocked.For(Run(new CheckResult(CheckId.Green, CheckLevel.Required, CheckVerdict.Pass, "green"), budget: null)));

    [Fact]
    public void ARequiredReviewThatNoKeyRanWaitsForAMaintainer()
    {
        JsonObject evidence = new()
        {
            ["schema"] = "lockflight.ci-evidence/v1",
            ["event"] = "pr",
            ["head"] = Head,
            ["feature"] = new JsonObject { ["id"] = "001-shipping" },
            ["checks"] = new JsonArray(
            [
                new JsonObject { ["id"] = "green", ["context"] = "gates", ["verdict"] = "PASS", ["requirement"] = "required", ["why"] = "green" },
                new JsonObject { ["id"] = "gates", ["context"] = "gates", ["verdict"] = "PASS", ["requirement"] = "required", ["why"] = "gates" },
                new JsonObject { ["id"] = "review", ["context"] = "lockflight/judge", ["verdict"] = "SKIPPED", ["requirement"] = "required", ["why"] = "expected" },
                .. OffChecks.Select(id => new JsonObject { ["id"] = id, ["context"] = "lockflight/judge", ["verdict"] = "N/A", ["requirement"] = "off", ["why"] = "off" }),
            ]),
            ["blocked"] = null,
            ["finishedAt"] = "2026-11-02T10:15:00Z",
        };

        JsonObject verdict = CiReportCommand.Join(evidence, Head, null);

        Assert.Equal("NOT-EARNED", verdict["verdict"]!.GetValue<string>());
        Assert.Equal("access-needed", verdict["blocked"]!["reason"]!["code"]!.GetValue<string>());
        Assert.Equal("grant-access", verdict["blocked"]!["needs"]![0]!["action"]!.GetValue<string>());
        Assert.Equal("001-shipping", verdict["blocked"]!["feature"]!.GetValue<string>());

        JsonArray checks = evidence["checks"]!.AsArray();
        checks.Remove(checks.Single(check => check!["id"]!.GetValue<string>() == "red"));
        checks.Add(new JsonObject { ["id"] = "red", ["context"] = "lockflight/judge", ["verdict"] = "NOT-EARNED", ["requirement"] = "required", ["why"] = "no red commit" });
        evidence["blocked"] = null;
        Assert.Null(CiReportCommand.Join(evidence, Head, null)["blocked"]);
    }

    private static JudgeRun Run(CheckResult check, JudgeBudget? budget)
    {
        JudgeRun run = CiEvidenceTests.Run();
        return run with { Findings = run.Findings with { Budget = budget }, Checks = [check] };
    }
}
