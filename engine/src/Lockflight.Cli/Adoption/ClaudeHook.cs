namespace Lockflight.Cli.Adoption;

/// <summary>
/// A command hook found in a settings file: where it is configured (<c>shell</c>, a unit path or <c>user</c>), its event and
/// matcher, its command, and the script the command runs when that resolves to a file.
/// </summary>
internal sealed record ClaudeHook(string Scope, string Event, string? Matcher, string Command, string? ScriptPath);
