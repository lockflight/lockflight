namespace Lockflight.Cli.Canaries;

/// <summary>One canary: the control and the violation planted against it, how it came out, and what the control answered.</summary>
internal sealed record CanaryReport(string Name, CanaryResult Result, string Detail);
