using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Evidence.Blocked;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The structure of <c>lockflight.ci-evidence/v1</c> the report passes on into the verdict of record, beyond the checks it reads
/// itself: the event a string, the change class, feature and host-tampering sections objects or null, the cycles an array, the
/// embedded gate result a <c>lockflight.result/v1</c> object, and the embedded blocked report a valid <c>lockflight.blocked/v1</c>
/// (read by the engine's own reader). Evidence that fails it is an ERROR, never a verdict.json that breaks the schema.
/// </summary>
internal static class EvidenceShape
{
    private const string ResultSchema = "lockflight.result/v1";

    /// <summary>What does not have the frozen shape; null when nothing.</summary>
    public static string? Problem(JsonObject evidence)
    {
        if (evidence["event"] is not JsonValue judgeEvent || judgeEvent.GetValueKind() != JsonValueKind.String)
        {
            return "'event' must be a string";
        }

        foreach (string property in new[] { "changeClass", "feature", "hostTampering", "policy", "engine", "base", "approvals", "specDiff" })
        {
            if (evidence.ContainsKey(property) && evidence[property] is not null and not JsonObject)
            {
                return $"'{property}' must be an object or null";
            }
        }

        foreach (string property in new[] { "cycles", "redCommits", "existingChanged", "builds" })
        {
            if (evidence.ContainsKey(property) && evidence[property] is not null and not JsonArray)
            {
                return $"'{property}' must be an array or null";
            }
        }

        if (evidence["gates"] is JsonNode gates && (gates is not JsonObject result || result["schema"] is not JsonValue schema || schema.GetValueKind() != JsonValueKind.String
            || schema.GetValue<string>() != ResultSchema))
        {
            return $"'gates' must be a {ResultSchema} object or null";
        }

        if (evidence["blocked"] is JsonNode blocked && BlockedReportReader.Read(blocked.ToJsonString()) is { Valid: false } reading)
        {
            return $"'blocked' is not a valid lockflight.blocked/v1 report ({string.Join("; ", reading.Problems.Take(3))})";
        }

        return null;
    }
}
