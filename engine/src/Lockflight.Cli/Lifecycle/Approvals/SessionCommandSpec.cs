namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// An in-session human command: its slash command without the slash (<c>lockflight:approve</c>), the engine command a person runs
/// instead in a terminal (<c>approve</c>, <c>redgreen reset</c>), what a refusal says was not done, the decision only the person
/// makes (for the guard that keeps the agent from queueing the command), and the prefix of a refusal's ledger fingerprint.
/// </summary>
internal sealed record SessionCommandSpec(string Name, string TerminalCommand, string NothingDone, string Decision, string RefusalPrefix);
