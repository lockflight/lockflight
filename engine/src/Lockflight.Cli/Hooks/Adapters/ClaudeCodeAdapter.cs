using System.Text.Json;
using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Hooks.Adapters;

/// <summary>
/// Translates a Claude Code hook payload into a <see cref="HookRequest"/>: the tool name to a <see cref="ToolKind"/>, the file a
/// tool names to <see cref="HookRequest.Paths"/>, <c>CLAUDE_PROJECT_DIR</c> to the session's project directory. The plugin's
/// shim keeps Claude Code's names for its own recovery decisions; a test keeps its lists in sync with <see cref="ToolKinds"/>.
/// </summary>
internal static class ClaudeCodeAdapter
{
    public const string ClientName = "claude-code";

    /// <summary>Claude Code's tool names and what each does; every other name (MCP tools included) is <see cref="ToolKind.Other"/>.</summary>
    public static IReadOnlyDictionary<string, (ToolKind Kind, ShellDialect? Shell)> ToolKinds { get; } =
        new Dictionary<string, (ToolKind, ShellDialect?)>(StringComparer.Ordinal)
        {
            ["Read"] = (ToolKind.Read, null),
            ["LS"] = (ToolKind.Read, null),
            ["Grep"] = (ToolKind.Search, null),
            ["Glob"] = (ToolKind.Search, null),
            ["Edit"] = (ToolKind.Edit, null),
            ["MultiEdit"] = (ToolKind.Edit, null),
            ["Write"] = (ToolKind.Write, null),
            ["NotebookEdit"] = (ToolKind.Notebook, null),
            ["Bash"] = (ToolKind.Shell, ShellDialect.Posix),
            ["PowerShell"] = (ToolKind.Shell, ShellDialect.PowerShell),
            ["WebFetch"] = (ToolKind.Fetch, null),
            ["WebSearch"] = (ToolKind.Fetch, null),
            ["Task"] = (ToolKind.Agent, null),
            ["Agent"] = (ToolKind.Agent, null),
        };

    /// <summary>The <c>tool_input</c> properties that name a file, in the order they are read.</summary>
    private static readonly string[] FileProperties = ["file_path", "notebook_path"];

    /// <summary>A path property an unknown tool may write (an MCP tool's <c>path</c>); it makes the call a file write.</summary>
    private const string OtherToolPathProperty = "path";

    /// <summary>The request for a Claude Code payload; null when it is not a JSON object.</summary>
    /// <param name="projectDirectory">Claude Code's <c>CLAUDE_PROJECT_DIR</c>; null or empty when it is not set.</param>
    public static HookRequest? Parse(string rawInput, string? projectDirectory)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawInput);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object ? Translate(root, projectDirectory) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>What a Claude Code tool name does; <see cref="ToolKind.Other"/> for a name this adapter does not know.</summary>
    public static (ToolKind Kind, ShellDialect? Shell) KindOf(string? toolName) =>
        toolName is not null && ToolKinds.TryGetValue(toolName, out (ToolKind Kind, ShellDialect? Shell) known) ? known : (ToolKind.Other, null);

    private static HookRequest Translate(JsonElement root, string? projectDirectory)
    {
        string? toolName = HookPayloadStrings.Read(root, "tool_name");
        (ToolKind kind, ShellDialect? shell) = KindOf(toolName);
        List<string> paths = [];
        string? command = null;
        List<string> inputStrings = [];
        if (root.TryGetProperty("tool_input", out JsonElement toolInput))
        {
            if (FileProperties.Select(property => HookPayloadStrings.Read(toolInput, property)).FirstOrDefault(path => path is not null) is string filePath)
            {
                paths.Add(filePath);
            }
            else if (kind == ToolKind.Other && HookPayloadStrings.Read(toolInput, OtherToolPathProperty) is string otherPath)
            {
                paths.Add(otherPath);
            }

            command = HookPayloadStrings.Read(toolInput, "command");
            HookPayloadStrings.Collect(toolInput, inputStrings);
        }

        bool writesFiles = kind is ToolKind.Edit or ToolKind.Write or ToolKind.Notebook || (kind == ToolKind.Other && toolName is not null && paths.Count > 0);
        HookPrompt? prompt = HookPayloadStrings.Read(root, "command_name") is string commandName
            ? new HookPrompt(commandName, HookPayloadStrings.Read(root, "command_source"), HookPayloadStrings.Read(root, "command_args"))
            : null;
        return new HookRequest(new HookTool(toolName, kind, shell, writesFiles), paths, command)
        {
            Session = new HookSession(
                HookPayloadStrings.Read(root, "session_id"),
                HookPayloadStrings.Read(root, "agent_type") ?? HookPayloadStrings.Read(root, "agent_id"),
                string.IsNullOrEmpty(projectDirectory) ? null : projectDirectory),
            WorkingDirectory = HookPayloadStrings.Read(root, "cwd"),
            Prompt = prompt,
            TaskSubject = HookPayloadStrings.Read(root, "task_subject"),
            InputStrings = inputStrings,
            Client = new HookClient(ClientName, Version: null),
        };
    }
}
