using Lockflight.Cli.Config;
using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CheckLevelsTests
{
    [Fact]
    public void ABehaviourChangeRequiresRedSinceRedHostTamperingGreenAndGates()
    {
        IReadOnlyDictionary<CheckId, CheckLevel> levels = For(ChangeClass.Behaviour, InterimShape.Other, JudgeEvent.PullRequest);

        Assert.All([CheckId.Red, CheckId.TestsSinceRed, CheckId.TestsHostTampering, CheckId.Green, CheckId.Gates], check => Assert.Equal(CheckLevel.Required, levels[check]));
        Assert.All([CheckId.TestsExistingChanged, CheckId.Trace, CheckId.Review, CheckId.Approvals], check => Assert.Equal(CheckLevel.Advisory, levels[check]));
    }

    [Fact]
    public void ALowRiskFixNeedsOnlyGreenAndGates()
    {
        IReadOnlyDictionary<CheckId, CheckLevel> levels = For(ChangeClass.Fix, InterimShape.LowRisk, JudgeEvent.PullRequest);

        Assert.Equal([CheckId.Green, CheckId.Gates], levels.Where(level => level.Value == CheckLevel.Required).Select(level => level.Key).Order());
        Assert.Equal(CheckLevel.Off, levels[CheckId.Red]);
        Assert.Equal(CheckLevel.Off, levels[CheckId.Trace]);
        Assert.Equal(CheckLevel.Off, levels[CheckId.Approvals]);
    }

    [Fact]
    public void ADependencyOnlyChangeNeedsNoRedCycle()
    {
        IReadOnlyDictionary<CheckId, CheckLevel> levels = For(ChangeClass.Behaviour, InterimShape.DependencyOnly, JudgeEvent.PullRequest);

        Assert.Equal(CheckLevel.Off, levels[CheckId.Red]);
        Assert.Equal(CheckLevel.Required, levels[CheckId.Green]);
        Assert.Equal(CheckLevel.Required, levels[CheckId.TestsHostTampering]);
    }

    [Fact]
    public void ACriticalChangeRequiresTheReviewTraceAndApprovals()
    {
        IReadOnlyDictionary<CheckId, CheckLevel> levels = For(ChangeClass.Critical, InterimShape.Other, JudgeEvent.PullRequest);

        Assert.Equal(CheckLevel.Required, levels[CheckId.Review]);
        Assert.Equal(CheckLevel.Required, levels[CheckId.Trace]);
        Assert.Equal(CheckLevel.Required, levels[CheckId.Approvals]);
    }

    [Theory]
    [InlineData("push")]
    [InlineData("merge-group")]
    public void APushOrMergeGroupNeedsGreenAndGatesOnly(string eventName)
    {
        IReadOnlyDictionary<CheckId, CheckLevel> levels = For(ChangeClass.Behaviour, InterimShape.Other, JudgeEventNames.Parse(eventName)!.Value);

        Assert.Equal([CheckId.Green, CheckId.Gates], levels.Where(level => level.Value != CheckLevel.Off).Select(level => level.Key).Order());
    }

    private static IReadOnlyDictionary<CheckId, CheckLevel> For(ChangeClass changeClass, InterimShape shape, JudgeEvent judgeEvent) =>
        CheckLevels.For(InterimRequirements.For(changeClass, shape), shape, judgeEvent);
}
