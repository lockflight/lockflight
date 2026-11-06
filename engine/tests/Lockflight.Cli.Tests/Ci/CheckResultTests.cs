using Lockflight.Cli.Ci;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CheckResultTests
{
    [Fact]
    public void ANewVerdictCarriesItsOwnReasonNotTheOldOne()
    {
        CheckResult notRun = new(CheckId.Review, CheckLevel.Required, CheckVerdict.Skipped, ReasonCode.ReviewNotRun, "no review ran");

        CheckResult refused = notRun.Judged(CheckVerdict.Error, ReasonCode.EvidenceInvalid, "the review's result was not read");

        Assert.Equal(CheckVerdict.Error, refused.Verdict);
        Assert.Equal(ReasonCode.EvidenceInvalid, refused.Reason);
        Assert.Equal(CheckLevel.Required, refused.Level);
    }

    [Fact]
    public void ACheckThatPassedCannotBecomeAnErrorWithoutAReason()
    {
        CheckResult passed = new(CheckId.Review, CheckLevel.Advisory, CheckVerdict.Pass, ReasonCode.None, "no findings");

        Assert.Throws<ArgumentException>(() => passed.Judged(CheckVerdict.Error, ReasonCode.None, "the review's result was not read"));
    }
}
