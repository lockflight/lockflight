namespace Lockflight.Cli.Hosting;

/// <summary>
/// A command group reserved for a person whatever verb follows (<c>approve xyz</c> included), except <paramref name="AgentVerbs"/>
/// (<c>approve verify</c>), so a verb the engine does not know is refused and blocked rather than let through.
/// </summary>
internal sealed record ReservedGroup(string Word, IReadOnlyList<string> AgentVerbs);
