using System.Text.Json.Nodes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What the judge found by building and running: green at head (null when the head was not judged), each red commit's judgement
/// (null when no red was re-run) and every build.
/// </summary>
internal sealed record JudgeFindings(GreenJudgement? Green, IReadOnlyList<CycleJudgement>? Cycles, IReadOnlyList<BuildRecord> Builds)
{
    /// <summary>The fingerprint of each test at head, by key (<see cref="TestFingerprints"/>).</summary>
    public IReadOnlyDictionary<string, string> HeadFingerprints { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The head commit the findings are about (the proof point of an amend).</summary>
    public string? HeadCommit { get; init; }

    /// <summary>Each accepted cycle test compared from its red to head; null when tests since red were not judged.</summary>
    public IReadOnlyList<SinceRedTest>? SinceRed { get; init; }

    /// <summary>The existing tests the pull request changed, re-checked on the base; null when the check did not run.</summary>
    public IReadOnlyList<ExistingChangedTest>? ExistingChanged { get; init; }

    /// <summary>The host-tampering check; null when it did not run.</summary>
    public TamperingJudgement? Tampering { get; init; }

    /// <summary>Trace against the spec; null when it did not run.</summary>
    public TraceJudgement? Trace { get; init; }

    /// <summary>The gates at head: verdict, line and their result file; null when they did not run.</summary>
    public (CheckVerdict Verdict, string Why, JsonNode? Result)? Gates { get; init; }

    /// <summary>The time budget the steps ran under; null when nothing was built.</summary>
    public JudgeBudget? Budget { get; init; }

    public static JudgeFindings None { get; } = new(null, null, []);
}
