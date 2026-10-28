namespace Lockflight.Cli.Review;

/// <summary>
/// Whether a finding's <c>specQuote</c> is text of the spec. Recorded for a person to read; it does not change a blocker/major
/// finding's outcome or the run's exit. An anchored MUST or SHALL quote lets a minor or nit finding whose reproduction fails be
/// raised to major (<see cref="Escalations"/>).
/// </summary>
internal enum SpecAnchor
{
    /// <summary>The finding gives no spec quote.</summary>
    None = 1,

    /// <summary>The quote appears word for word in the spec, after runs of whitespace are made one space.</summary>
    Anchored = 2,

    /// <summary>The quote does not appear in the spec, or is too short to say anything.</summary>
    NotAnchored = 3,
}
