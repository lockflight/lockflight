using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Evidence;

public sealed class BlockedReportTests : IDisposable
{
    private const string Valid = """
        {
          "schema": "lockflight.blocked/v1",
          "at": "2026-11-02T10:15:00Z",
          "source": "crew:spec-writer",
          "feature": "014-orders-export",
          "head": null,
          "reason": { "code": "question", "summary": "two readings of the export scope" },
          "needs": [ { "who": "person", "action": "answer", "detail": "Answer the questions", "humanOnly": true, "paths": [] } ],
          "questions": [ { "id": "q1", "text": "Export all orders or only paid ones?", "options": [ { "label": "Only paid", "recommended": true }, { "label": "All" } ] } ],
          "evidence": [],
          "resumable": true,
          "extra": "ignored"
        }
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-blocked-");

    [Fact]
    public void AReportTheEngineWritesReadsBackValid()
    {
        BlockedReport written = BlockedReport.Of(
            new DateTimeOffset(2026, 11, 2, 12, 15, 0, TimeSpan.FromHours(2)), "engine:redgreen-red", BlockedReasonCode.ApprovalMissing, "the spec has no valid approval",
            [BlockedNeed.Of(BlockedWho.Person, BlockedAction.Approve, "Review specs/f1/spec.md and approve the spec", humanOnly: true) with { Paths = ["specs/f1/spec.md"] }]) with
        {
            Feature = "f1",
            Head = new string('a', 40),
            Questions = [new BlockedQuestion("q1", "Which?", [new BlockedOption("A", Recommended: true), new BlockedOption("B", Recommended: false)])],
            Resumable = true,
        };

        string json = JsonFiles.Format(BlockedReportJson.Render(written));
        BlockedReading reading = BlockedReportReader.Read(json);

        Assert.True(reading.Valid, string.Join("; ", reading.Problems));
        Assert.StartsWith("{\n  \"schema\": \"lockflight.blocked/v1\",\n  \"at\": \"2026-11-02T10:15:00Z\"", json.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        BlockedReport read = reading.Report!;
        Assert.Equal((written.At, written.Source, written.Code, written.Summary, written.Feature, written.Head, written.Resumable), (read.At, read.Source, read.Code, read.Summary, read.Feature, read.Head, read.Resumable));
        Assert.Equal(written.Needs[0] with { Paths = [] }, read.Needs[0] with { Paths = [] });
        Assert.Equal(written.Needs[0].Paths, read.Needs[0].Paths);
        Assert.Equal(["A", "B"], read.Questions[0].Options.Select(option => option.Label));
        Assert.True(read.Questions[0].Options[0].Recommended);
    }

    [Fact]
    public void UnknownPropertiesAreIgnored() => Assert.True(BlockedReportReader.Read(Valid).Valid);

    [Fact]
    public void AnUnknownCodeIsKeptAsWrittenAndNamedAsAProblem()
    {
        BlockedReading reading = BlockedReportReader.Read(Valid.Replace("\"question\"", "\"rate-limited\"", StringComparison.Ordinal));

        Assert.Equal((BlockedReasonCode.Unknown, "rate-limited"), (reading.Report!.Code, reading.Report.CodeName));
        Assert.Contains("'reason.code' 'rate-limited' is not a code this engine knows", reading.Problems);
        Assert.False(reading.Valid);
    }

    [Theory]
    [InlineData("\"schema\": \"lockflight.blocked/v1\"", "\"schema\": \"lockflight.blocked/v2\"", "'schema' must be lockflight.blocked/v1")]
    [InlineData("\"at\": \"2026-11-02T10:15:00Z\"", "\"at\": \"yesterday\"", "'at' must be an RFC 3339 time such as 2026-11-02T10:15:00Z")]
    [InlineData("\"source\": \"crew:spec-writer\"", "\"source\": \"someone\"", "'source' must be ci, engine:<command>, crew:<skill> or autonomous")]
    [InlineData("\"humanOnly\": true, \"paths\": []", "\"humanOnly\": true, \"paths\": [], \"command\": \"rm -rf /\"",
        "'needs[0]' carries a 'command'; a blocked report names none (renderers derive it from the reason and the action)")]
    [InlineData("\"who\": \"person\"", "\"who\": \"robot\"", "'needs[0].who' 'robot' is not person, codeowner or maintainer")]
    [InlineData("{ \"label\": \"Only paid\", \"recommended\": true }, { \"label\": \"All\" }", "{ \"label\": \"All\" }, { \"label\": \"Only paid\", \"recommended\": true }",
        "the recommended option of 'questions[0]' must come first, and only one")]
    [InlineData("\"head\": null", "\"head\": \"abc\"", "'head' must be a 40-character commit or null")]
    [InlineData("\"resumable\": true", "\"resumable\": \"yes\"", "'resumable' must be true or false")]
    public void AReportOutsideTheContractNamesTheProblem(string from, string to, string problem)
    {
        BlockedReading reading = BlockedReportReader.Read(Valid.Replace(from, to, StringComparison.Ordinal));

        Assert.False(reading.Valid);
        Assert.Contains(problem, reading.Problems);
    }

    [Fact]
    public void AReportWithoutNeedsIsUnreadable()
    {
        BlockedReading reading = BlockedReportReader.Read(Valid.Replace(
            "[ { \"who\": \"person\", \"action\": \"answer\", \"detail\": \"Answer the questions\", \"humanOnly\": true, \"paths\": [] } ]", "[]", StringComparison.Ordinal));

        Assert.Null(reading.Report);
        Assert.Contains("'needs' must list at least one thing a person must do", reading.Problems);
    }

    [Fact]
    public async Task ValidateAcceptsAValidReport()
    {
        File.WriteAllText(Path.Combine(root.FullName, "blocked.json"), Valid);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["blocked", "validate", "blocked.json"], "", root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("blocked.json: valid lockflight.blocked/v1 (question, 1 need, 1 question)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateListsEveryProblemAndFails()
    {
        File.WriteAllText(Path.Combine(root.FullName, "blocked.json"), Valid.Replace("\"who\": \"person\"", "\"who\": \"robot\"", StringComparison.Ordinal));

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["blocked", "validate", "blocked.json"], "", root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("blocked.json: 'needs[0].who' 'robot' is not person, codeowner or maintainer", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateOfAMissingFileIsAUsageError()
    {
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["blocked", "validate", "nope.json"], "", root.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
