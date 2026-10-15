namespace Lockflight.Cli.Gates.Formatting;

/// <summary>A changed line whose formatting a diagnostic would change, which is what the format step reports.</summary>
internal sealed record FormatFinding(string File, int Line, string DiagnosticId);
