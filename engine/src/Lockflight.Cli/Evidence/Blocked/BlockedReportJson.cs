using System.Text.Json.Nodes;

namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>Writes a <see cref="BlockedReport"/> as <c>lockflight.blocked/v1</c> JSON, the schema id first.</summary>
internal static class BlockedReportJson
{
    public static JsonObject Render(BlockedReport report) => new()
    {
        ["schema"] = BlockedReport.Schema,
        ["at"] = EvidenceTime.Format(report.At),
        ["source"] = report.Source,
        ["feature"] = report.Feature,
        ["head"] = report.Head,
        ["reason"] = new JsonObject { ["code"] = report.CodeName, ["summary"] = report.Summary },
        ["needs"] = new JsonArray([.. report.Needs.Select(need => (JsonNode)new JsonObject
        {
            ["who"] = need.WhoName,
            ["action"] = need.ActionName,
            ["detail"] = need.Detail,
            ["humanOnly"] = need.HumanOnly,
            ["paths"] = new JsonArray([.. need.Paths.Select(path => (JsonNode)JsonValue.Create(path))]),
        })]),
        ["questions"] = new JsonArray([.. report.Questions.Select(question => (JsonNode)new JsonObject
        {
            ["id"] = question.Id,
            ["text"] = question.Text,
            ["options"] = new JsonArray([.. question.Options.Select(option => (JsonNode)Option(option))]),
        })]),
        ["evidence"] = new JsonArray([.. report.Evidence.Select(path => (JsonNode)JsonValue.Create(path))]),
        ["resumable"] = report.Resumable,
    };

    private static JsonObject Option(BlockedOption option)
    {
        JsonObject rendered = new() { ["label"] = option.Label };
        if (option.Recommended)
        {
            rendered["recommended"] = true;
        }

        return rendered;
    }
}
