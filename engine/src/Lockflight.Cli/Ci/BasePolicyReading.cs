namespace Lockflight.Cli.Ci;

/// <summary>The base policy, or why it could not be read (an invalid manifest on the base is a configuration error).</summary>
internal sealed record BasePolicyReading(BasePolicy? Policy, string? Error);
