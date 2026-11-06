using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What the red commits judged so far mean for the next one, kept as two separate sets: the new tests of every judged cycle,
/// rejected ones included (<see cref="EarlierNew"/>: never new again at a later red), and the accepted cycles (<see cref="Accepted"/>:
/// their tests are known, so a later red may not delete, skip or make them explicit). A cycle that did not compile, weakened the
/// suite or could not be listed has no tests and adds nothing to either.
/// </summary>
internal sealed class RedCycleHistory(TimeProvider time)
{
    private readonly HashSet<string> earlierNew = new(StringComparer.Ordinal);
    private readonly List<RedGreenCycle> accepted = [];

    public IReadOnlySet<string> EarlierNew => earlierNew;

    public IReadOnlyList<RedGreenCycle> Accepted => accepted;

    public void Add(CycleJudgement cycle) => Add(cycle.Red.Commit, cycle.Accepted, cycle.Tests);

    /// <summary>Adds a red judged before, from what that judgement found: whether it was accepted, and its new tests.</summary>
    public void Add(string commit, bool wasAccepted, IReadOnlyList<RedGreenTest> tests)
    {
        earlierNew.UnionWith(tests.Select(test => test.Key));
        if (wasAccepted)
        {
            accepted.Add(new RedGreenCycle(accepted.Count + 1, time.GetUtcNow(), GreenAt: null, tests) { Snapshot = commit });
        }
    }
}
