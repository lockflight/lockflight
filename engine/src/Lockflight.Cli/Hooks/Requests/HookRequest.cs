namespace Lockflight.Cli.Hooks.Requests;

/// <summary>
/// One hook call in the engine's own terms (<c>lockflight.hook-request/v1</c>), independent of the client that sent it. An
/// adapter (<see cref="Adapters.ClaudeCodeAdapter"/>) translates the client's payload into it; every handler and guard rule
/// works on this shape only. Not a frozen format: it is published as experimental until a second client's adapter exists.
/// </summary>
/// <param name="Paths">Every file the call names (an edit tool may name several); empty when it names none.</param>
/// <param name="Command">The shell command of a <see cref="ToolKind.Shell"/> call; null otherwise.</param>
internal sealed record HookRequest(HookTool Tool, IReadOnlyList<string> Paths, string? Command)
{
    public HookSession Session { get; init; } = HookSession.Unknown;

    /// <summary>The call's working directory; null when the client sent none.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The slash command a prompt expands; null for other events.</summary>
    public HookPrompt? Prompt { get; init; }

    /// <summary>The title of the task being marked completed; null for other events.</summary>
    public string? TaskSubject { get; init; }

    /// <summary>Every string in the raw tool input, decoded, at any depth (for checks that look at the whole call).</summary>
    public IReadOnlyList<string> InputStrings { get; init; } = [];

    public HookClient Client { get; init; } = new(Adapters.ClaudeCodeAdapter.ClientName, Version: null);

    /// <summary>The first file the call names; null when it names none.</summary>
    public string? FilePath => Paths.Count > 0 ? Paths[0] : null;
}
