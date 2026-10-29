namespace Lockflight.Cli.Ci;

/// <summary>
/// What the judge found by building and running: green at head (null when the head was not judged), each red commit's judgement
/// (null when no red was re-run) and every build.
/// </summary>
internal sealed record JudgeFindings(GreenJudgement? Green, IReadOnlyList<CycleJudgement>? Cycles, IReadOnlyList<BuildRecord> Builds)
{
    /// <summary>The fingerprint of each test at head, by key (<see cref="TestFingerprints"/>).</summary>
    public IReadOnlyDictionary<string, string> HeadFingerprints { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static JudgeFindings None { get; } = new(null, null, []);
}
