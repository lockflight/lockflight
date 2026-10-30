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

    public static JudgeFindings None { get; } = new(null, null, []);
}
