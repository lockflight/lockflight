namespace Lockflight.Cli.Hooks.Requests;

/// <summary>The session a hook call belongs to.</summary>
/// <param name="Id">The client's session id; null when it sent none.</param>
/// <param name="Agent">The subagent making the call; null for the main session.</param>
/// <param name="ProjectDirectory">The session's root, which stays put after a <c>cd</c> (Claude Code: <c>CLAUDE_PROJECT_DIR</c>); null when unknown.</param>
internal sealed record HookSession(string? Id, string? Agent, string? ProjectDirectory)
{
    public static HookSession Unknown { get; } = new(Id: null, Agent: null, ProjectDirectory: null);
}
