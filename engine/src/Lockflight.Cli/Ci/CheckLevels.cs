using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Maps a change class's requirements to the judge's checks for one event. Green and the gates count on every event; a push or a
/// merge-queue group needs nothing else, because the pull request was judged on its own run. Red/green, tests since red and
/// spec/trace apply only when production code lines changed (a docs-only or test-only pull request needs none), which the
/// routing requirements leave to the judge.
/// </summary>
internal static class CheckLevels
{
    public static IReadOnlyDictionary<CheckId, CheckLevel> For(RequirementSet requirements, bool productionChanged, JudgeEvent judgeEvent)
    {
        Dictionary<CheckId, CheckLevel> levels = Enum.GetValues<CheckId>().ToDictionary(check => check, _ => CheckLevel.Off);
        levels[CheckId.Green] = CheckLevel.Required;
        levels[CheckId.Gates] = Level(requirements.LevelOf(RequirementKind.Gates));
        if (judgeEvent != JudgeEvent.PullRequest)
        {
            levels[CheckId.Gates] = CheckLevel.Required;
            return levels;
        }

        RequirementLevel redGreen = requirements.LevelOf(RequirementKind.RedGreen);
        CheckLevel red = productionChanged && redGreen != RequirementLevel.Off ? CheckLevel.Required : CheckLevel.Off;
        levels[CheckId.Red] = red;
        levels[CheckId.TestsSinceRed] = red;
        levels[CheckId.Routing] = CheckLevel.Required;

        // Advisory until the closure fingerprint makes its false NOT-EARNED rate acceptable.
        levels[CheckId.TestsExistingChanged] = CheckLevel.Advisory;
        levels[CheckId.TestsHostTampering] = CheckLevel.Required;
        levels[CheckId.Trace] = productionChanged ? Level(requirements.LevelOf(RequirementKind.SpecAndTrace)) : CheckLevel.Off;
        levels[CheckId.Review] = Level(requirements.LevelOf(RequirementKind.Review));
        levels[CheckId.Approvals] = Level(requirements.LevelOf(RequirementKind.SpecFirst));
        return levels;
    }

    /// <summary>
    /// <see cref="RequirementLevel.WhenApplicable"/> is advisory until the check can tell whether it applies (trace: whether there is a
    /// spec); <see cref="RequirementLevel.RequiredWithKey"/> is required, and a review skipped for want of a key is then NOT-EARNED.
    /// </summary>
    private static CheckLevel Level(RequirementLevel level) => level switch
    {
        RequirementLevel.Required or RequirementLevel.RequiredWithKey => CheckLevel.Required,
        RequirementLevel.Advisory or RequirementLevel.WhenApplicable => CheckLevel.Advisory,
        _ => CheckLevel.Off,
    };
}
