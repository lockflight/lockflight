using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewResultParserTests
{
    [Fact]
    public void TheSchemaObjectInsideTheResultStringIsRead()
    {
        const string output = """
            {"type":"result","is_error":false,"duration_ms":8123,"total_cost_usd":0.0397,
             "result":"{\"verdict\":\"request_changes\",\"summary\":\"Two defects.\",\"findings\":[{\"severity\":\"major\",\"file\":\"src/A.cs\",\"line\":23,\"requirement\":\"FR-003\",\"title\":\"Boundary\",\"evidence\":\"> instead of >=\",\"reproduction\":\"[Fact]\\npublic void Free() => Assert.Equal(0m, 1m);\"},{\"severity\":\"nit\",\"file\":\"src/A.cs\",\"line\":1,\"title\":\"Naming\",\"evidence\":\"x\"}]}"}
            """;

        ReviewOutcome outcome = ReviewResultParser.Parse(output);

        Assert.Null(outcome.Error);
        Assert.Equal("request_changes", outcome.Verdict);
        Assert.Equal(0.0397m, outcome.CostUsd);
        Assert.Equal(2, outcome.Findings.Count);
        Assert.Equal(FindingSeverity.Major, outcome.Findings[0].Severity);
        Assert.True(outcome.Findings[0].NeedsReproduction);
        Assert.Equal("Free", Reproductions.MethodName(outcome.Findings[0].Reproduction!));
        Assert.False(outcome.Findings[1].NeedsReproduction);
    }

    [Fact]
    public void TokensAreSummedOverEveryModelTheRunUsed()
    {
        const string output = """
            {"type":"result","is_error":false,"duration_ms":1000,"total_cost_usd":0.5,
             "usage":{"input_tokens":1,"output_tokens":1},
             "modelUsage":{"claude-opus-5-5":{"inputTokens":100,"outputTokens":40,"cacheReadInputTokens":900,"cacheCreationInputTokens":50,"costUSD":0.4},
                           "claude-haiku-4-5":{"inputTokens":10,"outputTokens":2,"cacheReadInputTokens":0,"cacheCreationInputTokens":0,"costUSD":0.1}},
             "structured_output":{"verdict":"approve","summary":"Fine.","findings":[]}}
            """;

        ReviewUsage usage = ReviewResultParser.Parse(output).Usage;

        Assert.Equal(new ReviewUsage(110, 42, 900, 50), usage);
    }

    [Fact]
    public void TokensComeFromUsageWhenTheModelBreakdownIsMissing()
    {
        const string output = """
            {"is_error":false,"usage":{"input_tokens":7,"output_tokens":3,"cache_read_input_tokens":20,"cache_creation_input_tokens":5},
             "structured_output":{"verdict":"approve","summary":"Fine.","findings":[]}}
            """;

        Assert.Equal(new ReviewUsage(7, 3, 20, 5), ReviewResultParser.Parse(output).Usage);
    }

    [Fact]
    public void EachFindingGetsTheIdOfItsDimensionPlaceAndClaim()
    {
        const string output = """
            {"is_error":false,"structured_output":{"verdict":"request_changes","summary":"One.","findings":[
              {"severity":"major","file":"src/A.cs","line":23,"title":"Boundary","evidence":"x"}]}}
            """;

        ReviewFinding finding = Assert.Single(ReviewResultParser.Parse(output).Findings);

        Assert.Equal(FindingIds.Of(ReviewNames.GeneralDimension, "src/A.cs", 23, "Boundary"), finding.Id);
    }

    [Theory]
    [InlineData("""{"is_error":true,"result":"Credit balance is too low","total_cost_usd":0}""", "claude reported an error")]
    [InlineData("Not logged in", "no JSON")]
    [InlineData("""{"is_error":false,"result":"I could not finish"}""", "not JSON")]
    public void UnusableOutputIsAnError(string output, string expected) =>
        Assert.Contains(expected, ReviewResultParser.Parse(output).Error, StringComparison.Ordinal);

    [Fact]
    public void AsyncReproductionsAreRecognised() =>
        Assert.Equal("Works", Reproductions.MethodName("[Fact]\npublic async Task Works() { await Task.Yield(); }"));
}
