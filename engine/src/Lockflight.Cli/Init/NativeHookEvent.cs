namespace Lockflight.Cli.Init;

/// <summary>One Claude Code event the native level hooks: the engine's name for it, its matcher, time limit and failure mode.</summary>
/// <param name="Event">Claude Code's event name (<c>PreToolUse</c>).</param>
/// <param name="EngineEvent">The engine's name for it (<c>pre-tool-use</c>).</param>
/// <param name="Matcher">The tool matcher; null for an event without tools.</param>
/// <param name="TimeoutSeconds">The hook's time limit.</param>
/// <param name="BlockOnFailure">Whether a failed or timed-out hook blocks the call (the guard fails closed).</param>
internal sealed record NativeHookEvent(string Event, string EngineEvent, string? Matcher, int TimeoutSeconds, bool BlockOnFailure);
