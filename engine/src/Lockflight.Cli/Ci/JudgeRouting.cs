using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>The change's class and requirements as risk routing computed them for the judge, or why they could not be (an ERROR, never a lower class).</summary>
internal sealed record JudgeRouting(InterimClassification? Class, RequirementSet? Requirements, string? Error)
{
    public static JudgeRouting Routed(InterimClassification classification, RequirementSet requirements) => new(classification, requirements, null);

    public static JudgeRouting Failed(string error) => new(null, null, error);
}
