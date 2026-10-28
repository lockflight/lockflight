using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>
/// The hook's decisions, pinned end to end: exit code, standard output and error, and the ledger lines, for the guard's rules
/// and their order (plugin API and unknown event for every event; lockdown, else the engine pin, for PreToolUse only; then the
/// relay guard; then the human-only and protected-path rules, at the root and in agent worktrees).
/// </summary>
public sealed partial class HookDecisionGoldenTests
{
    private const string ManifestText = """
        version: 1
        protected:
          - "secrets/**"
        stop:
          gate: false
        """;

    private const string MismatchedPin = """{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "9.9.9", "commands": ["lockflight"] } } }""";

    private static readonly Dictionary<string, string> NoVariables = [];

    private static readonly Dictionary<string, string> OtherPluginApi = new() { [EnvironmentVariables.PluginApi] = "2" };

    private static readonly Scenario[] Scenarios =
    [
        new("an edit of a source file", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs")),
        new("a read of the manifest", "pre-tool-use", root => ToolCall(root, "Read", "lockflight.yaml")),
        new("a write under a manifest rule", "pre-tool-use", root => ToolCall(root, "Write", "secrets/key.txt")),
        new("an edit of the manifest", "pre-tool-use", root => ToolCall(root, "Edit", "lockflight.yaml")),
        new("a notebook edit of a protected file", "pre-tool-use", root => ToolCall(root, "NotebookEdit", "secrets/a.ipynb")),
        new("a redirect into a protected file", "pre-tool-use", root => Bash(root, "echo x > global.json")),
        new("a pipe into a protected file", "pre-tool-use", root => Bash(root, "cat src/A.cs | sort -o secrets/out.txt")),
        new("a human-only command", "pre-tool-use", root => Bash(root, "dotnet lockflight approve spec")),
        new("a human-only command with an unknown verb", "pre-tool-use", root => Bash(root, "lockflight approve xyz")),
        new("the agent's verify command", "pre-tool-use", root => Bash(root, "lockflight approve verify --base main")),
        new("a writer behind xargs", "pre-tool-use", root => Bash(root, "cat list | xargs rm")),
        new("a tree that holds protected files", "pre-tool-use", root => Bash(root, "rm -rf secrets")),
        new("the folder above the project", "pre-tool-use", root => Bash(root, "rm -rf ..")),
        new("an unknown variable", "pre-tool-use", root => Bash(root, "rm $SOMEWHERE/x")),
        new("a wildcard", "pre-tool-use", root => Bash(root, "rm secrets/*")),
        new("a PowerShell write", "pre-tool-use", root => PowerShell(root, "Set-Content -Path lockflight.yaml -Value x")),
        new("a PowerShell alias", "pre-tool-use", root => PowerShell(root, "rm secrets/key.txt")),
        new("a relayed session command", "pre-tool-use", root => Tool(root, "CronCreate", "prompt", "/lockflight:approve spec 123456")),
        new("a session command from an unnamed tool", "pre-tool-use", root => Tool(root, null, "prompt", "/lockflight:reset")),
        new("a session command in a file write", "pre-tool-use", root => Tool(root, "Write", "file_path", Path.Combine(root, "docs", "a.md"), "content", "/lockflight:approve spec")),
        new("a write to a prompt queue file", "pre-tool-use", root => ToolCall(root, "Write", ".claude/scheduled_tasks.json")),
        new("a protected file in an agent worktree", "pre-tool-use", root => ToolCall(root, "Edit", ".claude/worktrees/w/lockflight.yaml"), Setup: AddWorktree),
        new("input that is not JSON", "pre-tool-use", _ => "not json"),
        new("an invalid manifest, then a write", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: root => File.WriteAllText(Path.Combine(root, Manifest.FileName), "nonsense: 1\n")),
        new("an invalid manifest, then a read", "pre-tool-use", root => ToolCall(root, "Read", "src/A.cs"), Setup: root => File.WriteAllText(Path.Combine(root, Manifest.FileName), "nonsense: 1\n")),
        new("another plugin API, an edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Environment: OtherPluginApi),
        new("another plugin API, a read", "pre-tool-use", root => ToolCall(root, "Read", "src/A.cs"), Environment: OtherPluginApi),
        new("another plugin API, Stop", "stop", root => Event(root, "Stop"), Environment: OtherPluginApi),
        new("another plugin API, SessionStart", "session-start", root => Event(root, "SessionStart"), Environment: OtherPluginApi),
        new("another plugin API, TaskCompleted", "task-completed", root => Event(root, "TaskCompleted"), Environment: OtherPluginApi),
        new("another plugin API, an unrelated prompt", "user-prompt-expansion", root => Expansion(root, "commit"), Environment: OtherPluginApi),
        new("another plugin API, a session command", "user-prompt-expansion", root => Expansion(root, "lockflight:approve"), Environment: OtherPluginApi),
        new("another plugin API, lockdown", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: Lockdown, Environment: OtherPluginApi),
        new("an unknown event", "no-such-event", root => Event(root, "NoSuchEvent")),
        new("an unknown event without a working directory", "no-such-event", _ => "{}"),
        new("lockdown, an edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: Lockdown),
        new("lockdown, a read", "pre-tool-use", root => ToolCall(root, "Read", "lockflight.yaml"), Setup: Lockdown),
        new("lockdown, the recovery command", "pre-tool-use", root => Bash(root, "dotnet tool restore && dotnet lockflight link"), Setup: Lockdown),
        new("lockdown and a stale pin, an edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: root => { Lockdown(root); StalePin(root); }),
        new("lockdown and a stale pin, a read", "pre-tool-use", root => ToolCall(root, "Read", "src/A.cs"), Setup: root => { Lockdown(root); StalePin(root); }),
        new("lockdown, Stop", "stop", root => Event(root, "Stop"), Setup: Lockdown),
        new("a stale pin, an edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: StalePin),
        new("a stale pin, a read", "pre-tool-use", root => ToolCall(root, "Read", "src/A.cs"), Setup: StalePin),
        new("a stale pin, the recovery command", "pre-tool-use", root => Bash(root, "dotnet tool restore && dotnet lockflight link"), Setup: StalePin),
        new("a stale pin, the recovery command chained", "pre-tool-use", root => Bash(root, "dotnet tool restore && dotnet lockflight link && rm x"), Setup: StalePin),
        new("a stale pin and an unreadable red/green record, the recovery command", "pre-tool-use", root => Bash(root, "dotnet tool restore"), Setup: root => { StalePin(root); CorruptTestLock(root); }),
        new("a stale pin and a relayed session command", "pre-tool-use", root => Tool(root, "CronCreate", "prompt", "/lockflight:approve spec"), Setup: StalePin),
        new("a stale pin, Stop", "stop", root => Event(root, "Stop"), Setup: StalePin),
        new("an unreadable pin, an edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: root => File.WriteAllText(Path.Combine(root, "dotnet-tools.json"), "{ not json")),
        new("an unreadable red/green record, a test edit", "pre-tool-use", root => ToolCall(root, "Edit", "tests/ATests.cs"), Setup: CorruptTestLock),
        new("an unreadable red/green record, a source edit", "pre-tool-use", root => ToolCall(root, "Edit", "src/A.cs"), Setup: CorruptTestLock),
        new("a relayed session command and a protected write", "pre-tool-use", root => Tool(root, "Bash", "command", "echo /lockflight:approve spec > lockflight.yaml")),
        new("Stop with the stop gate off", "stop", root => Event(root, "Stop")),
        new("PostToolUse", "post-tool-use", root => ToolCall(root, "Edit", "lockflight.yaml")),
        new("SubagentStop", "subagent-stop", root => Event(root, "SubagentStop")),
        new("an unrelated prompt", "user-prompt-expansion", root => Expansion(root, "commit")),
        new("SessionStart", "session-start", root => Event(root, "SessionStart")),
        new("TaskCompleted without a task", "task-completed", root => Event(root, "TaskCompleted")),
    ];

    [Fact]
    public async Task EveryHookDecisionIsTheSameAsBefore()
    {
        StringBuilder rendered = new();
        foreach (Scenario scenario in Scenarios)
        {
            DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-hook-golden-");
            try
            {
                File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), ManifestText);
                scenario.Setup?.Invoke(root.FullName);
                (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(
                    ["hook", scenario.Event], scenario.Payload(root.FullName), root.FullName, scenario.Environment ?? NoVariables);
                rendered.Append($"## {scenario.Name} ({scenario.Event})\n");
                rendered.Append($"exit: {(int)exitCode} {exitCode}\n");
                rendered.Append($"stdout: {Normalize(output, root.FullName)}\n");
                rendered.Append($"stderr: {Normalize(error, root.FullName)}\n");
                foreach (string line in LedgerLines(root.FullName))
                {
                    rendered.Append($"ledger: {Normalize(line, root.FullName)}\n");
                }

                rendered.Append('\n');
            }
            finally
            {
                TempDirectories.DeleteRecursively(root);
            }
        }

        GoldenFiles.Verify("hook-decisions", rendered.ToString());
    }

    [Fact]
    public void EveryLedgerEventTypeKeepsItsContractName()
    {
        StringBuilder rendered = new();
        foreach (LedgerEventType type in Enum.GetValues<LedgerEventType>())
        {
            rendered.Append($"{(int)type} {type}: {LedgerEventTypeNames.ToName(type)}\n");
        }

        GoldenFiles.Verify("ledger-event-types", rendered.ToString());
    }

    private static IEnumerable<string> LedgerLines(string root)
    {
        string directory = Path.Combine(root, LedgerWriter.RelativeDirectory);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.GetFiles(directory, "*.jsonl").Order(StringComparer.Ordinal))
        {
            foreach (string line in File.ReadLines(file))
            {
                using JsonDocument document = JsonDocument.Parse(line);
                string[] fields = ["type", "signature", "summary", "tool", "agent"];
                yield return string.Join(" ", fields
                    .Where(field => document.RootElement.TryGetProperty(field, out _))
                    .Select(field => $"{field}={document.RootElement.GetProperty(field).GetString()}"));
            }
        }
    }

    /// <summary>The text with the scratch root, the engine version and, on Windows, path separators written the same everywhere.</summary>
    private static string Normalize(string text, string root)
    {
        string normalized = text
            .Replace(JsonEncodedText.Encode(root).ToString(), "<root>", StringComparison.Ordinal)
            .Replace(root, "<root>", StringComparison.Ordinal)
            .Replace(ToolPin.RunningVersion, "<version>", StringComparison.Ordinal);
        return OperatingSystem.IsWindows() ? WindowsSeparator().Replace(normalized.Replace("\\\\", "/", StringComparison.Ordinal), "/") : normalized;
    }

    /// <summary>A backslash that separates path segments, not one that starts a JSON escape (<c>\u003C</c>).</summary>
    [GeneratedRegex(@"\\(?!u[0-9A-Fa-f]{4})")]
    private static partial Regex WindowsSeparator();

    private static void Lockdown(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, ".lockflight"));
        File.WriteAllText(Path.Combine(root, ".lockflight", "lockdown"), string.Empty);
    }

    private static void StalePin(string root) => File.WriteAllText(Path.Combine(root, "dotnet-tools.json"), MismatchedPin);

    private static void CorruptTestLock(string root)
    {
        string feature = Path.Combine(root, ".lockflight", "features", "001-x");
        Directory.CreateDirectory(feature);
        File.WriteAllText(Path.Combine(feature, "redgreen.json"), "{ not json");
    }

    private static void AddWorktree(string root)
    {
        string worktree = Path.Combine(root, ".claude", "worktrees", "w");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: elsewhere\n");
        File.WriteAllText(Path.Combine(worktree, Manifest.FileName), ManifestText);
    }

    private static string ToolCall(string root, string tool, string relativePath) =>
        Tool(root, tool, "file_path", Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), "content", "x");

    private static string Bash(string root, string command) => Tool(root, "Bash", "command", command);

    private static string PowerShell(string root, string command) => Tool(root, "PowerShell", "command", command);

    private static string Tool(string root, string? tool, params string[] input)
    {
        Dictionary<string, object> payload = new() { ["hook_event_name"] = "PreToolUse", ["cwd"] = root, ["session_id"] = "s1" };
        if (tool is not null)
        {
            payload["tool_name"] = tool;
        }

        payload["tool_input"] = Enumerable.Range(0, input.Length / 2).ToDictionary(index => input[2 * index], index => input[(2 * index) + 1]);
        return JsonSerializer.Serialize(payload);
    }

    private static string Event(string root, string name) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["hook_event_name"] = name, ["cwd"] = root, ["session_id"] = "s1" });

    private static string Expansion(string root, string command) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["hook_event_name"] = "UserPromptExpansion",
        ["cwd"] = root,
        ["session_id"] = "s1",
        ["command_name"] = command,
        ["command_source"] = "plugin",
        ["command_args"] = "spec",
        ["prompt"] = $"/{command} spec",
    });

    private sealed record Scenario(
        string Name, string Event, Func<string, string> Payload, Action<string>? Setup = null, Dictionary<string, string>? Environment = null);
}
