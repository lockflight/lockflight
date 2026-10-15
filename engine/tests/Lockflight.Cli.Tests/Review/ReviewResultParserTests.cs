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
