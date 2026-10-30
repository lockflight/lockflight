using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgeVerdictsTests
{
    [Fact]
    public void ARequiredFailOutranksEveryOtherVerdict() =>
        Assert.Equal(CheckVerdict.Fail, JudgeVerdicts.Combine([Check(CheckId.Red, CheckVerdict.Error), Check(CheckId.Green, CheckVerdict.Fail), Check(CheckId.Trace, CheckVerdict.NotEarned)]));

    [Fact]
    public void ARequiredErrorOutranksNotEarned() =>
        Assert.Equal(CheckVerdict.Error, JudgeVerdicts.Combine([Check(CheckId.Red, CheckVerdict.NotEarned), Check(CheckId.Green, CheckVerdict.Error)]));

    [Theory]
    [InlineData("NOT-EARNED")]
    [InlineData("SKIPPED")]
    [InlineData("N/A")]
    public void ARequiredCheckThatDidNotVouchIsNotEarned(string verdict) =>
        Assert.Equal(CheckVerdict.NotEarned, JudgeVerdicts.Combine([Check(CheckId.Green, CheckVerdict.Pass), Check(CheckId.Review, CheckVerdictNames.Parse(verdict)!.Value)]));

    [Fact]
    public void AdvisoryAndOffChecksNeverChangeTheVerdict() =>
        Assert.Equal(CheckVerdict.Pass, JudgeVerdicts.Combine(
        [
            Check(CheckId.Green, CheckVerdict.Pass),
            Check(CheckId.Review, CheckVerdict.Fail, CheckLevel.Advisory),
            Check(CheckId.Trace, CheckVerdict.NotApplicable, CheckLevel.Off),
        ]));

    [Fact]
    public void TheGatesContextTakesOnlyTheGatesAndGreen()
    {
        CheckResult[] checks = [Check(CheckId.Green, CheckVerdict.Pass), Check(CheckId.Gates, CheckVerdict.Pass), Check(CheckId.Red, CheckVerdict.Fail)];

        Assert.Equal(CheckVerdict.Pass, JudgeVerdicts.Of(StatusContext.Gates, checks));
        Assert.Equal(CheckVerdict.Fail, JudgeVerdicts.Of(StatusContext.Judge, checks));
    }

    private static CheckResult Check(CheckId id, CheckVerdict verdict, CheckLevel level = CheckLevel.Required) => new(id, level, verdict, "why");
}
