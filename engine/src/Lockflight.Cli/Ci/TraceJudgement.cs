using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Ci;

/// <summary>Trace: the spec it was judged against, the verdict and its line, whether the check applies, and the matrix rows.</summary>
internal sealed record TraceJudgement(SpecResolution Spec, CheckVerdict Verdict, string Why, bool Applies, IReadOnlyList<TraceRow> Rows);
