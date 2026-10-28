namespace Lockflight.Cli.Review;

/// <summary>
/// Whether a blocker/major finding's <c>specQuote</c> is text of the spec. Recorded for a person to read; it does not change the
/// finding's outcome or the run's exit.
/// </summary>
internal enum SpecAnchor
{
    /// <summary>The finding gives no spec quote (or is minor or nit, which need none).</summary>
    None = 1,

    /// <summary>The quote appears word for word in the spec, after runs of whitespace are made one space.</summary>
    Anchored = 2,

    /// <summary>The quote does not appear in the spec, or is too short to say anything.</summary>
    NotAnchored = 3,
}
