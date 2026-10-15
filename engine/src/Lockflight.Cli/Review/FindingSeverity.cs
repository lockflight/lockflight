namespace Lockflight.Cli.Review;

/// <summary>How much a review finding matters; blocker and major findings need an engine-run reproduction.</summary>
internal enum FindingSeverity
{
    /// <summary>Must not ship: wrong behaviour on a main path, data loss, security.</summary>
    Blocker = 1,

    /// <summary>A requirement is violated or a defect is likely to reach users.</summary>
    Major = 2,

    /// <summary>A real but contained problem (edge case, missing validation, misleading code).</summary>
    Minor = 3,

    /// <summary>Style or naming; never blocks.</summary>
    Nit = 4,
}
