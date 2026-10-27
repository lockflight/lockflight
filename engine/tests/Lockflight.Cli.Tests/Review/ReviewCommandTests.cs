using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewCommandTests : IDisposable
{
    private const string Feature = "001-orders";
    private const string Person = "reviewer@example.com";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-review-");
    private readonly DirectoryInfo tools = Directory.CreateTempSubdirectory("lockflight-review-path-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeClaude claude = new();

    [Fact]
    public async Task ARunRecordsEvidenceWithTheBaseTheModelTheClaudeVersionAndTokens()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored"));

        (ExitCode exitCode, _, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Assert.Single(RunFiles())));
        JsonElement run = evidence.RootElement;
        Assert.Equal("lockflight.review/v2", run.GetProperty("schema").GetString());
        Assert.Equal(Feature, run.GetProperty("feature").GetString());
        Assert.Equal("main", run.GetProperty("base").GetProperty("ref").GetString());
        Assert.True(run.GetProperty("base").GetProperty("isMergeBase").GetBoolean());
        Assert.Equal(run.GetProperty("base").GetProperty("commit").GetString(), run.GetProperty("base").GetProperty("mergeBase").GetString());
        Assert.Equal("opus", run.GetProperty("model").GetString());
        Assert.Equal(3.00m, run.GetProperty("budgetUsd").GetDecimal());
        Assert.Equal("default", run.GetProperty("settings").GetProperty("source").GetString());
        Assert.Equal(FakeClaude.Version, run.GetProperty("claudeCodeVersion").GetString());
        Assert.Equal(1200, run.GetProperty("usage").GetProperty("inputTokens").GetInt64());
        Assert.Equal(0.25m, run.GetProperty("estimatedCostUsd").GetDecimal());
        Assert.True(run.GetProperty("ofRecord").GetBoolean());
        JsonElement finding = Assert.Single(run.GetProperty("findings").EnumerateArray().ToArray());
        Assert.Equal(FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 3, "Discount ignored"), finding.GetProperty("id").GetString());
        Assert.Equal("unverified", finding.GetProperty("outcome").GetString());
        Assert.Equal("none", finding.GetProperty("reproductionResult").GetString());
        Assert.Equal("open", finding.GetProperty("state").GetString());
    }

    [Fact]
    public async Task TheReviewerRunsWithTheManifestsModelAndBudget()
    {
        await ProjectAsync("version: 1\nreview:\n  model: sonnet\n  maxUsd: 1.50\n");
        claude.Answers();

        (ExitCode exitCode, string output, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        IReadOnlyList<string> call = claude.Calls.Single(arguments => arguments.Contains("-p"));
        Assert.Equal("sonnet", call[call.ToList().IndexOf("--model") + 1]);
        Assert.Equal("1.50", call[call.ToList().IndexOf("--max-budget-usd") + 1]);
        Assert.Contains("review of record", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACleanRunWithAnotherModelIsRecordedButNotOfRecord()
    {
        await ProjectAsync();
        claude.Answers();

        (ExitCode exitCode, string output, _) = await RunAsync(["review", "--model", "sonnet"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("not the review of record: model sonnet is not the review model opus", output, StringComparison.Ordinal);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Assert.Single(RunFiles())));
        Assert.False(evidence.RootElement.GetProperty("ofRecord").GetBoolean());
        Assert.Contains("model sonnet", evidence.RootElement.GetProperty("notOfRecord")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABaseThatMovedOnIsNotOfRecord()
    {
        await ProjectAsync();
        await GitAsync("checkout", "-q", "-b", "feature");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "feature work");
        await GitAsync("checkout", "-q", "main");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "main moved on");
        await GitAsync("checkout", "-q", "feature");
        claude.Answers();

        (ExitCode exitCode, string output, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("is not the merge base", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecentCommitOnTheBranchAsTheBaseIsNotOfRecord()
    {
        await ProjectAsync();
        await GitAsync("checkout", "-q", "-b", "feature");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "first feature commit");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "second feature commit");
        claude.Answers();

        (ExitCode exitCode, string output, _) = await RunAsync(["review", "--base", "HEAD~1"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("base HEAD~1", output, StringComparison.Ordinal);
        Assert.Contains("is not the merge base of HEAD and the target main", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTargetsMergeBaseOnAFeatureBranchIsOfRecord()
    {
        await ProjectAsync("version: 1\nreview:\n  target: main\n");
        await GitAsync("checkout", "-q", "-b", "feature");
        await GitAsync("commit", "-q", "--allow-empty", "-m", "feature commit");
        claude.Answers();

        (ExitCode exitCode, string output, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("is the merge base of HEAD and main", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDefaultTargetIsTheBranchOriginHeadPointsAt()
    {
        await ProjectAsync();
        await GitAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await GitAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        claude.Answers();

        (ExitCode exitCode, _, _) = await RunAsync(["review"]);
        (_, string status, _) = await RunAsync(["review", "status"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Assert.Single(RunFiles())));
        Assert.Equal("origin/main", evidence.RootElement.GetProperty("base").GetProperty("ref").GetString());
        Assert.Equal("origin/main", evidence.RootElement.GetProperty("base").GetProperty("target").GetString());
        Assert.Contains("base `origin/main`", status, StringComparison.Ordinal);
        Assert.Contains("target `origin/main`, merge base `", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABadBudgetIsAUsageError()
    {
        await ProjectAsync();

        (ExitCode exitCode, _, string error) = await RunAsync(["review", "--budget", "lots"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--budget", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALaterCleanRunDoesNotCloseAnOpenFinding()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored")).Answers();

        await RunAsync(["review"]);
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode exitCode, string output, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("Discount ignored", output, StringComparison.Ordinal);
        Assert.Contains("carried", output, StringComparison.Ordinal);
        Assert.Equal(2, RunFiles().Length);
        (_, string status, _) = await RunAsync(["review", "status"]);
        Assert.Contains("2 review runs", status, StringComparison.Ordinal);
        Assert.Contains("Open findings (1)", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOpenFindingRaisedAgainAsMinorStaysOpen()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored"))
            .Answers("""{"severity":"minor","file":"src/Orders.cs","line":3,"title":"Discount ignored","evidence":"probably fine"}""");

        await RunAsync(["review"]);
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode exitCode, string output, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("- major [unverified, open]", output, StringComparison.Ordinal);
        (_, string status, _) = await RunAsync(["review", "status"]);
        Assert.Contains("Open findings (1)", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReproductionThatCannotRunLeavesTheFindingUnverified()
    {
        await ProjectAsync();
        claude.Answers(
            """{"severity":"major","file":"src/Orders.cs","line":3,"title":"Discount ignored","evidence":"x","reproduction":"[Fact]\npublic void Applies() => Assert.Equal(90m, 100m);"}""");

        (ExitCode exitCode, _, _) = await RunAsync(["review"]);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        using JsonDocument evidence = JsonDocument.Parse(File.ReadAllText(Assert.Single(RunFiles())));
        JsonElement finding = Assert.Single(evidence.RootElement.GetProperty("findings").EnumerateArray().ToArray());
        Assert.Equal("unverified", finding.GetProperty("outcome").GetString());
        Assert.Equal("not-run", finding.GetProperty("reproductionResult").GetString());
    }

    [Fact]
    public async Task RunsRecordedBeforeTheRulesCountButAreNotOfRecord()
    {
        await ProjectAsync();
        Directory.CreateDirectory(ReviewsDirectory());
        File.WriteAllText(
            Path.Combine(ReviewsDirectory(), "20261020-100000.json"),
            """{"schema":"lockflight.review/v1","at":"2026-10-20T10:00:00.0000000+00:00","base":"main","model":"opus","costUsd":0.5,"findings":[]}""");

        (_, string status, _) = await RunAsync(["review", "status"]);

        Assert.Contains("1 review run (0 of record)", status, StringComparison.Ordinal);
        Assert.Contains("recorded before review-of-record rules existed", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MinorFindingsAreReportedAndNeverOpen()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Minor("Naming"));

        (ExitCode exitCode, _, _) = await RunAsync(["review"]);
        (_, string status, _) = await RunAsync(["review", "status"]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Open findings (0)", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DismissIsRefusedInsideClaudeCode()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored"));
        await RunAsync(["review"]);
        string id = FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 3, "Discount ignored");

        (ExitCode exitCode, _, string error) = await RunAsync(["review", "dismiss", id, "--reason", "Intended"], agent: true);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("'review dismiss' records a human decision", error, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(ReviewsDirectory(), "dismissals")));
    }

    [Fact]
    public async Task ADismissedFindingIsClosedWithWhoWhenAndWhy()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored")).Answers();
        await RunAsync(["review"]);
        string id = FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 3, "Discount ignored");

        (ExitCode dismissed, _, _) = await RunAsync(["review", "dismiss", id, "--reason", "The discount is applied upstream"]);
        time.Advance(TimeSpan.FromMinutes(5));
        (ExitCode next, _, _) = await RunAsync(["review"]);
        (_, string status, _) = await RunAsync(["review", "status"]);

        Assert.Equal(ExitCode.Pass, dismissed);
        Assert.Equal(ExitCode.Pass, next);
        using JsonDocument record = JsonDocument.Parse(File.ReadAllText(Path.Combine(ReviewsDirectory(), "dismissals", $"{id}.json")));
        Assert.Equal(Person, record.RootElement.GetProperty("by").GetString());
        Assert.Equal("The discount is applied upstream", record.RootElement.GetProperty("reason").GetString());
        Assert.Equal("2026-10-27T09:00:00.0000000+00:00", record.RootElement.GetProperty("at").GetString());
        Assert.Contains("Open findings (0)", status, StringComparison.Ordinal);
        Assert.Contains("Dismissed (1)", status, StringComparison.Ordinal);
        Assert.Contains("The discount is applied upstream", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DismissingAnUnknownFindingChangesNothing()
    {
        await ProjectAsync();

        (ExitCode exitCode, _, string error) = await RunAsync(["review", "dismiss", "0123456789ab", "--reason", "x"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("no open finding 0123456789ab", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DismissNeedsAReason()
    {
        await ProjectAsync();

        (ExitCode exitCode, _, string error) = await RunAsync(["review", "dismiss", "0123456789ab"]);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--reason", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLedgerRecordsTheRunAndTheDismissalWithoutFindingText()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored"));
        await RunAsync(["review"]);
        string id = FindingIds.Of(ReviewNames.GeneralDimension, "src/Orders.cs", 3, "Discount ignored");
        await RunAsync(["review", "dismiss", id, "--reason", "Secret reason"]);

        string[] lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory))));

        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.DoesNotContain("Discount ignored", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain("Secret reason", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain(Person, line, StringComparison.Ordinal));
        using JsonDocument completed = JsonDocument.Parse(lines[0]);
        Assert.Equal("review.completed", completed.RootElement.GetProperty("type").GetString());
        Assert.Equal("review:unverified", completed.RootElement.GetProperty("signature").GetString());
        Assert.True(completed.RootElement.GetProperty("ofRecord").GetBoolean());
        Assert.Equal(1, completed.RootElement.GetProperty("openUnverified").GetInt32());
        Assert.Equal(1200, completed.RootElement.GetProperty("inputTokens").GetInt64());
        using JsonDocument dismissal = JsonDocument.Parse(lines[1]);
        Assert.Equal("finding.dismissed", dismissal.RootElement.GetProperty("type").GetString());
        Assert.Equal(id, dismissal.RootElement.GetProperty("finding").GetString());
    }

    [Fact]
    public async Task StatusPrintsAPullRequestSummaryWithTheBaseAndTheRunCount()
    {
        await ProjectAsync();
        claude.Answers(FakeClaude.Unverified("Discount ignored"));
        await RunAsync(["review"]);

        (ExitCode exitCode, string status, _) = await RunAsync(["review", "status", "--feature", Feature]);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("base `main`", status, StringComparison.Ordinal);
        Assert.Contains("1 review run", status, StringComparison.Ordinal);
        Assert.Contains("major, unverified", status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusIsNotReservedForAPerson()
    {
        await ProjectAsync();

        (ExitCode exitCode, string status, _) = await RunAsync(["review", "status"], agent: true);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("0 review runs", status, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(tools);
    }

    private async Task ProjectAsync(string manifest = "version: 1\n")
    {
        await GitAsync("init", "-q", "-b", "main");
        await GitAsync("config", "user.email", Person);
        await GitAsync("config", "user.name", "Reviewing Person");
        await GitAsync("config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), manifest);
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", Feature));
        File.WriteAllText(Path.Combine(root.FullName, "specs", Feature, "spec.md"), "- **FR-001**: The total applies the discount.\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "src"));
        File.WriteAllText(Path.Combine(root.FullName, "src", "Orders.cs"), "namespace Shop;\n");
        await GitAsync("add", ".");
        await GitAsync("commit", "-q", "-m", "start");
        File.WriteAllText(Path.Combine(root.FullName, "src", "Orders.cs"), "namespace Shop;\n\npublic static class Orders { public static decimal Total(decimal price) => price; }\n");
        File.WriteAllText(Path.Combine(tools.FullName, OperatingSystem.IsWindows() ? "claude.exe" : "claude"), string.Empty);
    }

    private async Task GitAsync(params string[] arguments) =>
        await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);

    private string ReviewsDirectory() => Path.Combine(root.FullName, ".lockflight", "features", Feature, "reviews");

    private string[] RunFiles() => Directory.GetFiles(ReviewsDirectory(), "*.json");

    private async Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string[] args, bool agent = false)
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.Path] = tools.FullName };
        if (agent)
        {
            environment[EnvironmentVariables.ClaudeCode] = "1";
        }

        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, time)
        {
            Environment = EnvironmentReader.Of(environment),
            Claude = claude.RunAsync,
        };
        ExitCode exitCode = await CliApplication.RunAsync(args, context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }
}
