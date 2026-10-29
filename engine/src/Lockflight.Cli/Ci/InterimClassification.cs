using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>The interim classifier's result: the classification in the routing contract's shape, and the shape of the change.</summary>
internal sealed record InterimClassification(Classification Classification, InterimShape Shape);
