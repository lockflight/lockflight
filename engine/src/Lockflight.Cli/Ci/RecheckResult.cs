using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Ci;

/// <summary>One test re-checked on older code: the outcome, the class it got there when it ran, and why.</summary>
internal sealed record RecheckResult(string Key, RecheckOutcome Outcome, RedClass? Class, string Why);
