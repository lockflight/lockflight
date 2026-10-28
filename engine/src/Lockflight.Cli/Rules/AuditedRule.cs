namespace Lockflight.Cli.Rules;

/// <summary>
/// One rule found in an instruction file: where it is, its text, what backs it (<see cref="By"/>: the control and why, null for
/// guidance only) and, for guidance only, the cheapest deterministic control that could back it (null when none fits).
/// </summary>
internal sealed record AuditedRule(string File, int Line, string Text, RuleStatus Status, string? By, string? Suggestion);
