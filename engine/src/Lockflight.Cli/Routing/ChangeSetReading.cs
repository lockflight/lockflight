namespace Lockflight.Cli.Routing;

/// <summary>The result of <see cref="ChangeSetReader.ReadAsync"/>: its status, the change set when it was read, and what to do otherwise.</summary>
internal sealed record ChangeSetReading(ChangeSetReadStatus Status, ChangeSet? ChangeSet, string Message);
