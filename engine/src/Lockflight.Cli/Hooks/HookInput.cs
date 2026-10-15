using System.Text.Json;

namespace Lockflight.Cli.Hooks;

/// <summary>The fields of a Claude Code hook payload the engine uses.</summary>
internal sealed record HookInput(string? WorkingDirectory, string? ToolName, string? FilePath, string? Command)
{
    /// <summary>The Claude Code session id (<c>session_id</c>).</summary>
    public string? SessionId { get; init; }

    /// <summary>The subagent making the call (<c>agent_type</c>, else <c>agent_id</c>); null in the main session.</summary>
    public string? Agent { get; init; }

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
            if (root.TryGetProperty("tool_input", out JsonElement toolInput))
            {
                filePath = ReadString(toolInput, "file_path") ?? ReadString(toolInput, "notebook_path");
                command = ReadString(toolInput, "command");
            }

            return new HookInput(ReadString(root, "cwd"), ReadString(root, "tool_name"), filePath, command)
            {
                SessionId = ReadString(root, "session_id"),
                Agent = ReadString(root, "agent_type") ?? ReadString(root, "agent_id"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
