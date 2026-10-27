using System.Text.Json;

namespace Lockflight.Cli.Hooks;

/// <summary>The fields of a Claude Code hook payload the engine uses.</summary>
internal sealed record HookInput(string? WorkingDirectory, string? ToolName, string? FilePath, string? Command)
{
    /// <summary>The Claude Code session id (<c>session_id</c>).</summary>
    public string? SessionId { get; init; }

    /// <summary>The subagent making the call (<c>agent_type</c>, else <c>agent_id</c>); null in the main session.</summary>
    public string? Agent { get; init; }

    /// <summary>The slash command a typed prompt expands (<c>command_name</c>, e.g. <c>lockflight:approve</c>); null for other events.</summary>
    public string? CommandName { get; init; }

    /// <summary>Where that command comes from (<c>command_source</c>: <c>plugin</c>, …).</summary>
    public string? CommandSource { get; init; }

    /// <summary>What the person typed after the command (<c>command_args</c>).</summary>
    public string? CommandArguments { get; init; }

    /// <summary>The title of the task being marked completed (<c>task_subject</c>); null for other events.</summary>
    public string? TaskSubject { get; init; }

    /// <summary>Every string value inside <c>tool_input</c>, decoded, at any depth (for checks that look at the whole call).</summary>
    public IReadOnlyList<string> ToolInputStrings { get; init; } = [];

    /// <summary>Parses the hook JSON; null when it is not a JSON object.</summary>
    public static HookInput? Parse(string rawInput)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawInput);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? filePath = null;
            string? command = null;
            List<string> toolInputStrings = [];
            if (root.TryGetProperty("tool_input", out JsonElement toolInput))
            {
                filePath = ReadString(toolInput, "file_path") ?? ReadString(toolInput, "notebook_path");
                command = ReadString(toolInput, "command");
                CollectStrings(toolInput, toolInputStrings);
            }

            return new HookInput(ReadString(root, "cwd"), ReadString(root, "tool_name"), filePath, command)
            {
                SessionId = ReadString(root, "session_id"),
                Agent = ReadString(root, "agent_type") ?? ReadString(root, "agent_id"),
                CommandName = ReadString(root, "command_name"),
                CommandSource = ReadString(root, "command_source"),
                CommandArguments = ReadString(root, "command_args"),
                TaskSubject = ReadString(root, "task_subject"),
                ToolInputStrings = toolInputStrings,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CollectStrings(JsonElement element, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    strings.Add(property.Name);
                    CollectStrings(property.Value, strings);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectStrings(item, strings);
                }

                break;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
