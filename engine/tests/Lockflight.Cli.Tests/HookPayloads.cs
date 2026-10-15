using System.Text.Json;

namespace Lockflight.Cli.Tests;

/// <summary>Builds hook payloads shaped like Claude Code's.</summary>
internal static class HookPayloads
{
    public static string ToolCall(string toolName, string filePath, string workingDirectory) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["hook_event_name"] = "PreToolUse",
        ["cwd"] = workingDirectory,
        ["tool_name"] = toolName,
        ["tool_input"] = new Dictionary<string, string> { ["file_path"] = filePath, ["content"] = "x" },
    });
}
