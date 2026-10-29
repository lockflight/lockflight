namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>
/// A suppression one line holds, before it is placed in a file: the rule, the kind and — for an analyzer config line — the setting's key
/// and severity (0 none … 4 error), so a change that lowers an existing setting can be told from one that keeps it.
/// </summary>
internal sealed record SuppressionHit(string Rule, SuppressionKind Kind, string? SettingKey = null, int? Severity = null);
