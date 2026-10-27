namespace Lockflight.Cli.Review;

/// <summary>A person's dismissal of a finding: what it was (copied when dismissed), who dismissed it (git <c>user.email</c>), when and why.</summary>
internal sealed record FindingDismissal(string FindingId, FindingSeverity Severity, string File, int Line, string Title, string By, DateTimeOffset At, string Reason);
