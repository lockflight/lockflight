namespace Lockflight.Cli.Hooks.Requests;

/// <summary>The agent client that sent the hook call, as its adapter names it (<c>claude-code</c>).</summary>
internal sealed record HookClient(string Name, string? Version);
