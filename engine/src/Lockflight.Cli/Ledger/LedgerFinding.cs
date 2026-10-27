namespace Lockflight.Cli.Ledger;

/// <summary>Which review finding a ledger line is about: its feature, id, severity and dimension (never its text, nor who dismissed it).</summary>
internal sealed record LedgerFinding(string Feature, string Id, string Severity, string Dimension);
