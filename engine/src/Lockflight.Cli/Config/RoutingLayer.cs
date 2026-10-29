namespace Lockflight.Cli.Config;

/// <summary>One entry of <c>routing.layers</c>, inner layers first: a reference from an inner layer to an outer one is critical.</summary>
internal sealed record RoutingLayer(string Name, IReadOnlyList<string> Paths);
