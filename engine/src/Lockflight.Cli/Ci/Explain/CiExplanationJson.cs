using System.Text.Json.Nodes;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// <c>ci explain --json</c>: the explanations as one JSON object for a tool or an agent. Not a frozen contract: the shape may
/// change between versions (the evidence and its reason codes are the contract).
/// </summary>
internal static class CiExplanationJson
{
    public static JsonObject Render(IReadOnlyList<CiExplanation> explanations) => new()
    {
        ["explanations"] = new JsonArray([.. explanations.Select(Explanation)]),
    };

    private static JsonNode Explanation(CiExplanation explanation) => new JsonObject
    {
        ["source"] = explanation.Source,
        ["kind"] = ExplainedDocumentNames.ToName(explanation.Document),
        ["head"] = explanation.Head,
        ["verdict"] = explanation.Verdict,
        ["contexts"] = explanation.ContextsNode(),
        ["checks"] = new JsonArray([.. explanation.Checks.Select(Check)]),
        ["blocked"] = explanation.Blocked is ExplainedBlock blocked
            ? new JsonObject
            {
                ["code"] = blocked.CodeName,
                ["summary"] = blocked.Summary,
                ["needs"] = new JsonArray([.. blocked.Needs.Select(need => (JsonNode?)need)]),
                ["next"] = blocked.Code == BlockedReasonCode.Unknown ? null : BlockedNextSteps.Next(blocked.Code),
                ["link"] = BlockedNextSteps.Link(blocked.Code),
            }
            : null,
    };

    private static JsonNode Check(ExplainedCheck check) => new JsonObject
    {
        ["id"] = check.Id,
        ["title"] = check.Title,
        ["requirement"] = check.Requirement,
        ["verdict"] = check.Verdict,
        ["detail"] = check.Detail,
        ["reason"] = check.Reason.Name,
        ["known"] = check.Reason.Known,
        ["next"] = check.Reason.Next,
        ["link"] = check.Reason.Link,
        ["steps"] = new JsonArray([.. check.Steps.Select(Check)]),
    };
}
