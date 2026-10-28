namespace Lockflight.Cli.Docs;

/// <summary>One reference that does not resolve: where it is (repository-relative file, 1-based line), what it says and why it fails.</summary>
internal sealed record DriftFinding(string File, int Line, string Reference, string Reason, DriftKind Kind);
