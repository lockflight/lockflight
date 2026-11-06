namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// One reason the test projects are locked: a feature's red/green record (its path and feature id), why it locks (an open cycle,
/// or a record that cannot be read), and whether it is unreadable, which only a person's <c>redgreen reset</c> clears.
/// </summary>
internal sealed record TestLockHolder(string RecordPath, string FeatureId, string Reason, bool Unreadable);
