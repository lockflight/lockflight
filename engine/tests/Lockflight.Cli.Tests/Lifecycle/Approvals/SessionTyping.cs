using System.Text.Json;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

/// <summary>Types an in-session human command as Claude Code's UserPromptExpansion hook payload, in an attended session unless told otherwise.</summary>
internal static class SessionTyping
{
    public static IReadOnlyDictionary<string, string> AttendedSession { get; } = new Dictionary<string, string>
    {
        [EnvironmentVariables.ClaudeCodeEntrypoint] = "cli",
        [EnvironmentVariables.ClaudeCodeSessionAttended] = "1",
        [EnvironmentVariables.ClaudeCode] = "1",
    };

    public static async Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(
        string root, TimeProvider time, RandomSource codes, string commandName, string arguments, IReadOnlyDictionary<string, string>? environment = null)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["session_id"] = "session-1",
            ["cwd"] = root,
            ["hook_event_name"] = "UserPromptExpansion",
            ["expansion_type"] = "slash_command",
            ["command_name"] = commandName,
            ["command_args"] = arguments,
            ["command_source"] = "plugin",
            ["prompt"] = $"/{commandName} {arguments}",
        });
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(payload), output, error, root, time)
        {
            Environment = EnvironmentReader.Of(environment ?? AttendedSession),
            Random = codes,
        };

        ExitCode exitCode = await new HookCommand(context).RunAsync(HookEvent.UserPromptExpansion, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    /// <summary>The agent's turn ends: Claude Code runs the Stop hook. Returns its standard output.</summary>
    public static Task<string> EndTurnAsync(string root, TimeProvider time) => EndTurnAsync(root, time, "session-1", AttendedSession);

    /// <summary>A turn of session <paramref name="sessionId"/> ends, in a session described by <paramref name="environment"/>.</summary>
    public static async Task<string> EndTurnAsync(string root, TimeProvider time, string sessionId, IReadOnlyDictionary<string, string> environment)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["session_id"] = sessionId,
            ["cwd"] = root,
            ["hook_event_name"] = "Stop",
        });
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(payload), output, error, root, time) { Environment = EnvironmentReader.Of(environment) };
        ExitCode exitCode = await new HookCommand(context).RunAsync(HookEvent.Stop, TestContext.Current.CancellationToken);
        Assert.True(exitCode == ExitCode.Pass, error.ToString());
        return output.ToString().Trim();
    }

    /// <summary>The message a recorded decision shows the person.</summary>
    public static string SystemMessage(string output) => JsonDocument.Parse(output).RootElement.GetProperty("systemMessage").GetString()!;
}
