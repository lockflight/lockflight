namespace Lockflight.Cli.Ledger;

/// <summary>
/// Who made a blocked call and what it was: the tool, the command or the file path it named, the session and, for a subagent,
/// its type or id. The ledger is readable by the agent, so a command is shortened with long token-like strings masked; a file
/// path is only shortened, because a path made of random-looking folder names (a macOS temporary folder) is not a credential.
/// </summary>
internal sealed record LedgerEventContext(string? Tool, string? Command, string? FilePath, string? Session, string? Agent);
