using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The change's class in the routing contract's shape, how it was computed (<see cref="Source"/>: <c>routing</c> from the base
/// branch's routing policy, or <c>interim</c> from git alone when there is no base to read a change set against), the interim
/// shape, and whether production code lines changed: red/green and spec/trace apply only then.
/// </summary>
internal sealed record InterimClassification(Classification Classification, InterimShape Shape)
{
    public const string RoutingSource = "routing";

    public string Source { get; init; } = InterimClassifier.Source;

    public bool ProductionChanged { get; init; } = Shape == InterimShape.Other;
}
