namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>A code waiting for its second step, read without checking a code: what it was shown for and until when it is valid.</summary>
internal sealed record SessionCodeWaiting(string Subject, IReadOnlyList<string> Payload, DateTimeOffset ExpiresAt);
