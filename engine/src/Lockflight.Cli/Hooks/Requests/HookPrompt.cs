namespace Lockflight.Cli.Hooks.Requests;

/// <summary>The slash command a typed prompt expands (prompt expansion only).</summary>
/// <param name="Name">The command, such as <c>lockflight:approve</c>.</param>
/// <param name="Source">Where the command comes from (<c>plugin</c>, …).</param>
/// <param name="Arguments">What the person typed after the command.</param>
internal sealed record HookPrompt(string? Name, string? Source, string? Arguments);
