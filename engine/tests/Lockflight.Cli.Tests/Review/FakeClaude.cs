using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Review;

/// <summary>Stands in for <c>claude</c>: answers <c>--version</c>, and each <c>-p</c> call with the next queued review.</summary>
internal sealed class FakeClaude
{
    public const string Version = "2.1.0 (Claude Code)";

    private readonly Queue<string> reviews = new();

    public List<IReadOnlyList<string>> Calls { get; } = [];

    /// <summary>Queues a review answering with these findings (JSON objects of the review schema).</summary>
    public FakeClaude Answers(params string[] findings)
    {
        string verdict = findings.Length == 0 ? "approve" : "request_changes";
        string usage = """{"claude-opus-5-5":{"inputTokens":1200,"outputTokens":300,"cacheReadInputTokens":0,"cacheCreationInputTokens":0}}""";
        string review = $$"""{"verdict":"{{verdict}}","summary":"Reviewed.","findings":[{{string.Join(',', findings)}}]}""";
        reviews.Enqueue($$"""{"type":"result","is_error":false,"duration_ms":4000,"total_cost_usd":0.25,"modelUsage":{{usage}},"structured_output":{{review}}}""");
        return this;
    }

    public Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, TimeSpan timeLimit, string? standardInputPath, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        string output = arguments is ["--version"] ? Version : reviews.Dequeue();
        return Task.FromResult(new ProcessResult(0, output, string.Empty, 10, TimedOut: false));
    }

    /// <summary>An unverified major finding: it has no reproduction, so no tests are built.</summary>
    public static string Unverified(string title) =>
        $$"""{"severity":"major","file":"src/Orders.cs","line":3,"requirement":"FR-001","title":"{{title}}","evidence":"the total ignores the discount"}""";

    public static string Minor(string title) =>
        $$"""{"severity":"minor","file":"src/Orders.cs","line":1,"title":"{{title}}","evidence":"naming"}""";
}
