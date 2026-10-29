using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgeBudgetTests
{
    [Fact]
    public void AStepStartsOnlyWhileMoreThanTheReserveIsLeft()
    {
        ManualTime time = new(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero));
        JudgeBudget budget = new(TimeSpan.FromMinutes(10), time);

        Assert.True(budget.TryStart("head"));
        time.Advance(TimeSpan.FromMinutes(7));

        Assert.False(budget.TryStart("red 1"));
        Assert.True(budget.Exhausted);
        Assert.Equal(["red 1"], budget.NotRun);
    }

    [Fact]
    public void AProcessIsBoundedByTheTimeLeftBeforeTheReserve()
    {
        ManualTime time = new(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero));
        JudgeBudget budget = new(TimeSpan.FromMinutes(10), time);
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(2), budget.ProcessLimit(TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromSeconds(30), budget.ProcessLimit(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task ABudgetTooSmallForAnyBuildLeavesEveryUndecidedCheckNotEarnedAndSaysSo()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using GitHistoryFixture repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        DirectoryInfo evidence = Directory.CreateTempSubdirectory("lockflight-ci-budget-");
        try
        {
            string main = await repository.Write("lockflight.yaml", "version: 1\nci:\n  timeoutMinutes: 2\n").CommitAsync("init", cancellationToken);
            await repository.CheckoutNewAsync("001-x", cancellationToken);
            string head = await repository.Write("a.cs", "a").CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");

            (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.FullName], string.Empty, repository.Root);

            JsonNode document = JsonNode.Parse(File.ReadAllText(Path.Combine(evidence.FullName, "evidence.json")))!;
            Assert.Equal(ExitCode.NotEarned, exitCode);
            Assert.True(document["budget"]!["exhausted"]!.GetValue<bool>());
            Assert.Contains("head", document["budget"]!["notRun"]!.AsArray().Select(step => step!.GetValue<string>()));
            JsonNode green = document["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "green")!;
            Assert.Contains("the CI budget (2 min, ci.timeoutMinutes on the target branch) ran out after 0 builds", green["why"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            TempDirectories.DeleteRecursively(evidence);
        }
    }
}
