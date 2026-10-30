namespace Lockflight.Cli.Routing;

/// <summary>The result of <see cref="RoutingPolicyReader.ReadAsync"/>: its status, the policy when there is one, and what went wrong otherwise.</summary>
internal sealed record RoutingPolicyReading(RoutingPolicyReadStatus Status, RoutingPolicy? Policy, string Message);
