using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CiReportTests : IDisposable
{
    private static readonly string Head = new('a', 40);

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-ci-report-");

    [Fact]
    public void AMissingJudgeArtifactIsNotEarned()
    {
        JsonObject verdict = CiReportCommand.Join(null, Head, null);

        Assert.Equal("NOT-EARNED", verdict["verdict"]!.GetValue<string>());
        Assert.Equal("NOT-EARNED", verdict["contexts"]!["gates"]!.GetValue<string>());
        Assert.Contains("the judge did not finish", CommentRenderer.Next(verdict), StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceAboutAnotherHeadIsAnError() =>
        Assert.Equal("ERROR", CiReportCommand.Join(Evidence(), new string('b', 40), null)["verdict"]!.GetValue<string>());

    [Fact]
    public void EvidenceWithoutEveryCheckIsAnErrorNotAPass()
    {
        JsonObject evidence = Evidence();
        evidence["checks"] = new JsonArray();

        Assert.Equal("ERROR", CiReportCommand.Join(evidence, Head, null)["contexts"]!["gates"]!.GetValue<string>());
    }

    [Fact]
    public void EvidenceThatMakesTheGatesAdvisoryIsAnError()
    {
        JsonObject evidence = Evidence();
        evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "gates")!["requirement"] = "advisory";

        Assert.Equal("ERROR", CiReportCommand.Join(evidence, Head, null)["verdict"]!.GetValue<string>());
    }

    [Fact]
    public void AFieldOfTheWrongTypeIsAnErrorNotACrash()
    {
        JsonObject evidence = Evidence();
        evidence["checks"]![0]!["verdict"] = 3;

        Assert.Equal("ERROR", CiReportCommand.Join(evidence, Head, null)["verdict"]!.GetValue<string>());
    }

    [Fact]
    public void AFailingReviewFailsTheJudgeContextOnlyWhereTheClassRequiresIt()
    {
        JsonObject review = new() { ["schema"] = "lockflight.ci-review/v1", ["head"] = Head, ["verdict"] = "FAIL", ["why"] = "1 confirmed blocker" };

        JsonObject verdict = CiReportCommand.Join(Evidence(reviewLevel: "required"), Head, review);

        Assert.Equal("PASS", verdict["contexts"]!["gates"]!.GetValue<string>());
        Assert.Equal("FAIL", verdict["contexts"]!["lockflight/judge"]!.GetValue<string>());
        Assert.Equal("Review: 1 confirmed blocker", CommentRenderer.Next(verdict));
    }

    [Fact]
    public void AReviewOfAnotherHeadIsIgnoredAsStale()
    {
        JsonObject review = new() { ["schema"] = "lockflight.ci-review/v1", ["head"] = new string('c', 40), ["verdict"] = "PASS" };

        JsonObject verdict = CiReportCommand.Join(Evidence(reviewLevel: "required"), Head, review);

        JsonNode check = verdict["checks"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "review")!;
        Assert.Equal("SKIPPED", check["verdict"]!.GetValue<string>());
        Assert.Contains("stale", check["why"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("NOT-EARNED", verdict["contexts"]!["lockflight/judge"]!.GetValue<string>());
    }

    [Fact]
    public void WithoutAReviewAnAdvisoryReviewDoesNotBlock() =>
        Assert.Equal("PASS", CiReportCommand.Join(Evidence(), Head, null)["verdict"]!.GetValue<string>());

    [Fact]
    public void TheCommentIsPinnedAndEscapesTextFromThePullRequest() =>
        GoldenFiles.Verify("ci-comment", CommentRenderer.Render(CiReportCommand.Join(Evidence(testName: "Shop.Tests:@org/team <img src=x> `tick` | pipe"), Head, null), CommentRenderer.MaxCommentLength));

    [Fact]
    public void ALongCommentDropsSectionsButKeepsTheTableAndTheNextLine()
    {
        JsonObject evidence = Evidence(redVerdict: "FAIL");
        JsonArray tests = evidence["cycles"]![0]!["tests"]!.AsArray();
        for (int index = 0; index < 2000; index++)
        {
            tests.Add(new JsonObject { ["key"] = $"Shop.Tests:Shop.Tests.Many.Test{index}{new string('x', 40)}", ["red"] = "RED_STUB" });
        }

        string comment = CommentRenderer.Render(CiReportCommand.Join(evidence, Head, null), CommentRenderer.MaxCommentLength);

        Assert.True(comment.Length <= CommentRenderer.MaxCommentLength, $"{comment.Length} characters");
        Assert.Contains("> **Next:** Red at red commits", comment, StringComparison.Ordinal);
        Assert.Contains("| Check | Verdict |", comment, StringComparison.Ordinal);
        Assert.Contains("… more in the job summary", comment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportWritesTheVerdictCommentAndSummaryAndExitsWithTheContextsVerdict()
    {
        string judge = Path.Combine(directory.FullName, "judge");
        string output = Path.Combine(directory.FullName, "out");
        JsonFiles.Write(Path.Combine(judge, "evidence.json"), Evidence(redVerdict: "FAIL"));

        (ExitCode gates, _, _) = await TestConsole.RunAsync(["ci", "report", "--evidence", judge, "--head", Head, "--out", output, "--context", "gates"], string.Empty, directory.FullName);
        (ExitCode judgeContext, string printed, _) = await TestConsole.RunAsync(["ci", "report", "--evidence", judge, "--head", Head, "--out", output, "--context", "lockflight/judge"], string.Empty, directory.FullName);

        Assert.Equal(ExitCode.Pass, gates);
        Assert.Equal(ExitCode.Fail, judgeContext);
        Assert.Contains("FAIL lockflight/judge", printed, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "verdict.json")));
        Assert.StartsWith("<!-- lockflight:ci:v1 head=", File.ReadAllText(Path.Combine(output, "comment.md")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, "summary.md")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private static JsonObject Evidence(string reviewLevel = "advisory", string redVerdict = "PASS", string testName = "Shop.Tests:Shop.Tests.PriceTests.Shipping") => new()
    {
        ["schema"] = "lockflight.ci-evidence/v1",
        ["engine"] = new JsonObject { ["version"] = "0.3.0" },
        ["event"] = "pr",
        ["head"] = Head,
        ["base"] = new JsonObject { ["sha"] = new string('b', 40) },
        ["policy"] = new JsonObject { ["from"] = new string('b', 40) },
        ["feature"] = new JsonObject { ["id"] = "001-shipping" },
        ["changeClass"] = new JsonObject { ["class"] = "behaviour", ["policy"] = new JsonObject { ["preset"] = "interim" } },
        ["cycles"] = new JsonArray(new JsonObject
        {
            ["label"] = "1",
            ["commit"] = new string('d', 40),
            ["verdict"] = redVerdict,
            ["tests"] = new JsonArray(new JsonObject { ["key"] = testName, ["red"] = "RED_STUB", ["sinceRed"] = "unchanged", ["atHead"] = "pass" }),
        }),
        ["trace"] = new JsonObject { ["rows"] = new JsonArray(new JsonObject { ["requirement"] = "FR-001", ["tests"] = new JsonArray("PriceTests.Shipping"), ["status"] = "covered" }) },
        ["checks"] = new JsonArray(
            Check("red", "lockflight/judge", redVerdict, "required", redVerdict == "PASS" ? "1 cycles, 1 tests, each red in production code" : "cycle 1 (ddddddd): GREEN_BEFORE"),
            Check("green", "gates", "PASS", "required", "2 tests, 2 test cases run, 0 failed"),
            Check("gates", "gates", "PASS", "required", "format PASS · build PASS · test PASS"),
            Check("review", "lockflight/judge", "SKIPPED", reviewLevel, "the report joins the review job's result"),
            Check("approvals", "lockflight/judge", "N/A", "off", "off for this change class"),
            Check("tests.sinceRed", "lockflight/judge", "PASS", "required", "1 unchanged, 0 amends accepted"),
            Check("tests.existingChanged", "lockflight/judge", "PASS", "advisory", "no existing test changed"),
            Check("tests.hostTampering", "lockflight/judge", "PASS", "required", "no code in the test assemblies that could tamper with the test host"),
            Check("trace", "lockflight/judge", "PASS", "required", "1/1 requirements of 001-shipping")),
    };

    private static JsonObject Check(string id, string context, string verdict, string level, string why) =>
        new() { ["id"] = id, ["context"] = context, ["verdict"] = verdict, ["requirement"] = level, ["why"] = why };
}
