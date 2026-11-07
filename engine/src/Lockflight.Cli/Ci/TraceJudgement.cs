using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Ci;

/// <summary>Trace: the spec it was judged against, the verdict and its line, whether the check applies, and the matrix rows.</summary>
internal sealed record TraceJudgement(SpecResolution Spec, CheckVerdict Verdict, ReasonCode Reason, string Why, bool Applies, IReadOnlyList<TraceRow> Rows)
{
    /// <summary>
    /// Markers added, changed or removed on requirements the base's spec already had (<see cref="RequirementMarkers"/>), for the
    /// comment to call out.
    /// </summary>
    public IReadOnlyList<string> MarkerChanges { get; init; } = [];

    /// <summary>The structural tests new since the fork point and how each did there and at head; empty when there are none.</summary>
    public IReadOnlyList<StructuralTest> Structural { get; init; } = [];
}
