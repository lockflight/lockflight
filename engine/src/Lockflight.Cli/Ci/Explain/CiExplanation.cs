using System.Text.Json.Nodes;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// What <c>ci explain</c> makes of one file: where it came from, what kind it is, the head and verdict it states, the status
/// contexts, each check that did not pass, and who the run waits for. <see cref="Origin"/> says, for a run's artifact, which
/// job wrote it and how far it is trusted; null for a file the person named.
/// </summary>
internal sealed record CiExplanation(
    string Source,
    ExplainedDocument Document,
    string? Head,
    string Verdict,
    IReadOnlyList<KeyValuePair<string, string>> Contexts,
    IReadOnlyList<ExplainedCheck> Checks,
    ExplainedBlock? Blocked)
{
    public string? Origin { get; init; }

    /// <summary>The contexts as JSON, in the order the file lists them.</summary>
    public JsonObject ContextsNode() => new([.. Contexts.Select(context => KeyValuePair.Create(context.Key, (JsonNode?)context.Value))]);
}
