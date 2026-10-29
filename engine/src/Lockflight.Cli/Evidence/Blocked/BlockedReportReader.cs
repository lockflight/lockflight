using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// Reads <c>lockflight.blocked/v1</c>. A tolerant reader: unknown properties are ignored, and an unknown code, person or action is
/// kept as written (a decision treats it as unknown). A missing required property makes the report unreadable, and is named. The
/// problems it collects are what <c>lockflight blocked validate</c> reports, so a crew skill can check what it wrote.
/// </summary>
internal static partial class BlockedReportReader
{
    public static BlockedReading Read(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return Read(document.RootElement);
        }
        catch (JsonException exception)
        {
            return new BlockedReading(null, [$"not valid JSON: {exception.Message}"]);
        }
    }

    private static BlockedReading Read(JsonElement root)
    {
        List<string> problems = [];
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new BlockedReading(null, ["the report must be a JSON object"]);
        }

        if (String(root, "schema") != BlockedReport.Schema)
        {
            return new BlockedReading(null, [$"'schema' must be {BlockedReport.Schema}"]);
        }

        DateTimeOffset? at = EvidenceTime.Parse(String(root, "at"));
        string? source = String(root, "source");
        JsonElement reason = Property(root, "reason", JsonValueKind.Object);
        string? codeName = String(reason, "code");
        string? summary = String(reason, "summary");
        Require(at is not null, "'at' must be an RFC 3339 time such as 2026-11-02T10:15:00Z", problems);
        Require(source is not null && SourcePattern().IsMatch(source), "'source' must be ci, engine:<command>, crew:<skill> or autonomous", problems);
        Require(codeName is not null && summary is not null, "'reason' needs a 'code' and a 'summary'", problems);
        Require(codeName is null || BlockedNames.CodeOf(codeName) != BlockedReasonCode.Unknown, $"'reason.code' '{codeName}' is not a code this engine knows", problems);
        Require(OptionalString(root, "feature"), "'feature' must be a string or null", problems);
        Require(String(root, "head") is not string head || HeadPattern().IsMatch(head), "'head' must be a 40-character commit or null", problems);
        Require(OptionalString(root, "head"), "'head' must be a string or null", problems);
        Require(Property(root, "resumable", JsonValueKind.True).ValueKind != JsonValueKind.Undefined
            || Property(root, "resumable", JsonValueKind.False).ValueKind != JsonValueKind.Undefined, "'resumable' must be true or false", problems);

        List<BlockedNeed> needs = ReadNeeds(Property(root, "needs", JsonValueKind.Array), problems);
        List<BlockedQuestion> questions = ReadQuestions(root, problems);
        List<string> evidence = Strings(root, "evidence", problems);
        if (at is null || source is null || codeName is null || summary is null || needs.Count == 0)
        {
            return new BlockedReading(null, problems);
        }

        BlockedReport report = new(at.Value, source, BlockedNames.CodeOf(codeName), codeName, summary, needs)
        {
            Feature = String(root, "feature"),
            Head = String(root, "head"),
            Questions = questions,
            Evidence = evidence,
            Resumable = Property(root, "resumable", JsonValueKind.True).ValueKind == JsonValueKind.True,
        };
        return new BlockedReading(report, problems);
    }

    private static List<BlockedNeed> ReadNeeds(JsonElement array, List<string> problems)
    {
        List<BlockedNeed> needs = [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            problems.Add("'needs' must list at least one thing a person must do");
            return needs;
        }

        int index = 0;
        foreach (JsonElement need in array.EnumerateArray())
        {
            string where = $"needs[{index++}]";
            string? who = String(need, "who");
            string? action = String(need, "action");
            string? detail = String(need, "detail");
            JsonElement humanOnly = need.ValueKind == JsonValueKind.Object && need.TryGetProperty("humanOnly", out JsonElement flag) ? flag : default;
            Require(need.ValueKind != JsonValueKind.Object || !need.TryGetProperty("command", out _),
                $"'{where}' carries a 'command'; a blocked report names none (renderers derive it from the reason and the action)", problems);
            if (who is null || action is null || detail is null || humanOnly.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                problems.Add($"'{where}' needs 'who', 'action', 'detail' and 'humanOnly'");
                continue;
            }

            Require(BlockedNames.WhoOf(who) != BlockedWho.Unknown, $"'{where}.who' '{who}' is not person, codeowner or maintainer", problems);
            Require(BlockedNames.ActionOf(action) != BlockedAction.Unknown, $"'{where}.action' '{action}' is not an action this engine knows", problems);
            needs.Add(new BlockedNeed(BlockedNames.WhoOf(who), who, BlockedNames.ActionOf(action), action, detail, humanOnly.ValueKind == JsonValueKind.True)
            {
                Paths = Strings(need, "paths", problems),
            });
        }

        return needs;
    }

    private static List<BlockedQuestion> ReadQuestions(JsonElement root, List<string> problems)
    {
        List<BlockedQuestion> questions = [];
        JsonElement array = Property(root, "questions", JsonValueKind.Array);
        if (array.ValueKind != JsonValueKind.Array)
        {
            Require(Property(root, "questions", JsonValueKind.Undefined).ValueKind == JsonValueKind.Undefined, "'questions' must be a list", problems);
            return questions;
        }

        int index = 0;
        foreach (JsonElement question in array.EnumerateArray())
        {
            string where = $"questions[{index++}]";
            string? id = String(question, "id");
            string? text = String(question, "text");
            JsonElement options = Property(question, "options", JsonValueKind.Array);
            if (id is null || text is null || options.ValueKind != JsonValueKind.Array || options.GetArrayLength() == 0)
            {
                problems.Add($"'{where}' needs an 'id', a 'text' and at least one option");
                continue;
            }

            List<BlockedOption> read = [.. options.EnumerateArray().Select(option =>
                new BlockedOption(String(option, "label") ?? string.Empty, Property(option, "recommended", JsonValueKind.True).ValueKind == JsonValueKind.True))];
            Require(read.All(option => option.Label.Length > 0), $"every option of '{where}' needs a 'label'", problems);
            Require(read.Skip(1).All(option => !option.Recommended), $"the recommended option of '{where}' must come first, and only one", problems);
            questions.Add(new BlockedQuestion(id, text, read));
        }

        return questions;
    }

    private static List<string> Strings(JsonElement element, string name, List<string> problems)
    {
        JsonElement array = Property(element, name, JsonValueKind.Array);
        if (array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [.. array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
        Require(values.Count == array.GetArrayLength(), $"every entry of '{name}' must be a string", problems);
        return values;
    }

    private static void Require(bool condition, string problem, List<string> problems)
    {
        if (!condition)
        {
            problems.Add(problem);
        }
    }

    private static JsonElement Property(JsonElement element, string name, JsonValueKind kind) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && (kind == JsonValueKind.Undefined || value.ValueKind == kind)
            ? value
            : default;

    private static string? String(JsonElement element, string name) =>
        Property(element, name, JsonValueKind.String) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>Absent, null or a string.</summary>
    private static bool OptionalString(JsonElement element, string name) =>
        element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    [GeneratedRegex(@"^(ci|autonomous|engine:[a-z0-9-]+|crew:[a-z0-9-]+)$")]
    private static partial Regex SourcePattern();

    [GeneratedRegex(@"^[0-9a-f]{40}$")]
    private static partial Regex HeadPattern();
}
