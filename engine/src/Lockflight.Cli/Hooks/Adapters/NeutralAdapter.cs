using System.Text.Json;
using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>
/// Reads a <c>lockflight.hook-request/v1</c> JSON payload as it is (<c>LOCKFLIGHT_HOOK_ADAPTER=neutral</c>), for tests and for
/// adapters that translate outside the engine. Its <c>event</c> must be the event the engine was called for.
/// </summary>
internal static class NeutralAdapter
{
    public const string Name = "neutral";
    public const string Schema = "lockflight.hook-request/v1";

    /// <summary>The request; null when the payload is not a <c>lockflight.hook-request/v1</c> object for <paramref name="hookEvent"/>.</summary>
    public static HookRequest? Parse(string rawInput, HookEvent hookEvent)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawInput);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || HookPayloadStrings.Read(root, "schema") != Schema
                || HookPayloadStrings.Read(root, "event") != EventName(hookEvent))
            {
                return null;
            }

            return Translate(root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The neutral name of an event (<c>pre-tool</c>); null for one this engine does not know.</summary>
    public static string? EventName(HookEvent hookEvent) => hookEvent switch
    {
        HookEvent.SessionStart => "session-start",
        HookEvent.PreToolUse => "pre-tool",
        HookEvent.PostToolUse => "post-tool",
        HookEvent.UserPromptExpansion => "prompt-expansion",
        HookEvent.TaskCompleted => "task-completed",
        HookEvent.Stop => "stop",
        HookEvent.SubagentStop => "subagent-stop",
        _ => null,
    };

    private static HookRequest? Translate(JsonElement root)
    {
        HookTool tool = HookTool.None;
        if (root.TryGetProperty("tool", out JsonElement toolElement) && toolElement.ValueKind == JsonValueKind.Object)
        {
            if (!ToolKindNames.TryParse(HookPayloadStrings.Read(toolElement, "kind"), out ToolKind kind))
            {
                return null;
            }

            ShellDialect? shell = HookPayloadStrings.Read(toolElement, "shell") switch
            {
                "posix" => ShellDialect.Posix,
                "powershell" => ShellDialect.PowerShell,
                _ => null,
            };
            if (kind == ToolKind.Shell && shell is null)
            {
                return null;
            }

            bool writesFiles = toolElement.TryGetProperty("writesFiles", out JsonElement flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? flag.ValueKind == JsonValueKind.True
                : kind is ToolKind.Edit or ToolKind.Write or ToolKind.Notebook;
            tool = new HookTool(HookPayloadStrings.Read(toolElement, "name"), kind, kind == ToolKind.Shell ? shell : null, writesFiles);
        }

        JsonElement session = root.TryGetProperty("session", out JsonElement sessionElement) ? sessionElement : default;
        JsonElement prompt = root.TryGetProperty("prompt", out JsonElement promptElement) ? promptElement : default;
        JsonElement task = root.TryGetProperty("task", out JsonElement taskElement) ? taskElement : default;
        JsonElement client = root.TryGetProperty("client", out JsonElement clientElement) ? clientElement : default;
        return new HookRequest(tool, Strings(root, "paths"), HookPayloadStrings.Read(root, "command"))
        {
            Session = new HookSession(HookPayloadStrings.Read(session, "id"), Agent(HookPayloadStrings.Read(session, "agent")), HookPayloadStrings.Read(session, "projectDir")),
            WorkingDirectory = HookPayloadStrings.Read(root, "cwd"),
            Prompt = prompt.ValueKind == JsonValueKind.Object
                ? new HookPrompt(HookPayloadStrings.Read(prompt, "name"), HookPayloadStrings.Read(prompt, "source"), HookPayloadStrings.Read(prompt, "args"))
                : null,
            TaskSubject = HookPayloadStrings.Read(task, "subject"),
            InputStrings = Strings(root, "inputStrings"),
            Client = new HookClient(HookPayloadStrings.Read(client, "name") ?? Name, HookPayloadStrings.Read(client, "version")),
        };
    }

    /// <summary>The main session is <c>main</c> in the neutral shape and null inside the engine.</summary>
    private static string? Agent(string? agent) => agent is null or "main" ? null : agent;

    private static IReadOnlyList<string> Strings(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];
}
