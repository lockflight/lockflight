using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// The reason a check or step named, as written (<see cref="Name"/>, null when it named none) and as this engine reads it
/// (<see cref="Code"/>, <see cref="ReasonCode.Unknown"/> for a name it does not know or none).
/// </summary>
internal sealed record ExplainedReason(string? Name, ReasonCode Code)
{
    /// <summary>Whether this engine's catalogue has an entry for the reason.</summary>
    public bool Known => Code is not (ReasonCode.None or ReasonCode.Unknown);

    /// <summary>The catalogue's Next line; null for a reason without an entry.</summary>
    public string? Next => Known ? ReasonCatalogue.Next(Code) : null;

    /// <summary>The reason's entry in the reasons guide, or the guide itself for a reason without one.</summary>
    public string Link => ReasonCatalogue.Link(Code) ?? ReasonCatalogue.GuideUrl;

    public static ExplainedReason Of(string? name) => new(name, ReasonCodeNames.Parse(name));
}
