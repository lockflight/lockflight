namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// A file <c>ci explain</c> reads: its path, the name shown for it, and for a run's artifact which job wrote it and how far it
/// is trusted (null for a file or folder the person named).
/// </summary>
internal sealed record ExplainedSource(string File, string Shown, string? Origin);
