namespace Lockflight.Cli.Workspace;

/// <summary>The shell's policy for a unit, or why it could not be read (never "no policy": the caller fails closed).</summary>
internal sealed record ShellPolicyReading(ShellPolicy? Policy, string? Error);
