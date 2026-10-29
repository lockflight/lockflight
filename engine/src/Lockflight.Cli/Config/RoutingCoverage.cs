namespace Lockflight.Cli.Config;

/// <summary>The patch coverage thresholds per change class (<c>routing.coverage</c>), whole percentages that never decrease from fix to critical.</summary>
internal sealed record RoutingCoverage(int Fix, int Behaviour, int Feature, int Critical);
