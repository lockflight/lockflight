namespace Lockflight.Cli.Review;

/// <summary>Where a blocker or major finding stands across review runs; minor, nit and rejected findings are not tracked.</summary>
internal enum FindingState
{
    /// <summary>Confirmed, contested or unverified and neither fixed nor dismissed: it is carried into every later run.</summary>
    Open = 1,

    /// <summary>A later run re-ran its reproduction and it passed.</summary>
    Fixed = 2,

    /// <summary>A person dismissed it with <c>lockflight review dismiss</c>.</summary>
    Dismissed = 3,
}
