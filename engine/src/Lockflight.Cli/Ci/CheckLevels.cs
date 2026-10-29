using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Maps a change class's requirements to the judge's checks for one event. Green and the gates count on every event; a push or a
/// merge-queue group needs nothing else, because the pull request was judged on its own run.
/// </summary>
internal static class CheckLevels
{
    public static IReadOnlyDictionary<CheckId, CheckLevel> For(RequirementSet requirements, InterimShape shape, JudgeEvent judgeEvent)
    {
        Dictionary<CheckId, CheckLevel> levels = Enum.GetValues<CheckId>().ToDictionary(check => check, _ => CheckLevel.Off);
        levels[CheckId.Green] = CheckLevel.Required;
        levels[CheckId.Gates] = Level(requirements.LevelOf(RequirementKind.Gates));
        if (judgeEvent != JudgeEvent.PullRequest)
        {
            levels[CheckId.Gates] = CheckLevel.Required;
            return levels;
        }

        CheckLevel redGreen = Level(requirements.LevelOf(RequirementKind.RedGreen));
        levels[CheckId.Red] = redGreen;
        levels[CheckId.TestsSinceRed] = redGreen;

        // Advisory until the closure fingerprint makes its false NOT-EARNED rate acceptable.
        levels[CheckId.TestsExistingChanged] = shape == InterimShape.LowRisk ? CheckLevel.Off : CheckLevel.Advisory;
        levels[CheckId.TestsHostTampering] = shape == InterimShape.LowRisk ? CheckLevel.Off : CheckLevel.Required;
        levels[CheckId.Trace] = Level(requirements.LevelOf(RequirementKind.SpecAndTrace));
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
