using System.Text.RegularExpressions;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Reasons;

namespace Lockflight.Cli.Tests.Reasons;

public sealed partial class ReasonCodeTests
{
    [Fact]
    public void EveryCodeButNoneAndUnknownHasOneUniqueContractName()
    {
        ReasonCode[] expected = [.. Enum.GetValues<ReasonCode>().Where(code => code is not (ReasonCode.None or ReasonCode.Unknown))];

        Assert.Equal(expected.Order(), ReasonCodeNames.Named.Order());
        Assert.Equal(expected.Length, expected.Select(ReasonCodeNames.ToName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(expected, code => Assert.Matches(ContractName(), ReasonCodeNames.ToName(code)!));
    }

    [Fact]
    public void ANameParsesBackToItsCodeAndAnythingElseIsUnknown()
    {
        Assert.All(ReasonCodeNames.Named, code => Assert.Equal(code, ReasonCodeNames.Parse(ReasonCodeNames.ToName(code))));
        Assert.Equal(ReasonCode.Unknown, ReasonCodeNames.Parse("NOT_A_CODE"));
        Assert.Equal(ReasonCode.Unknown, ReasonCodeNames.Parse(null));
        Assert.Null(ReasonCodeNames.ToName(ReasonCode.None));
        Assert.Null(ReasonCodeNames.ToName(ReasonCode.Unknown));
    }

    [Fact]
    public void ARedClassThatIsRejectedHasTheReasonOfTheSameName()
    {
        foreach (RedClass redClass in Enum.GetValues<RedClass>())
        {
            ReasonCode reason = CheckReasons.Of(redClass);
            Assert.Equal(RedClassNames.IsAccepted(redClass) ? null : RedClassNames.ToName(redClass), ReasonCodeNames.ToName(reason));
        }
    }

    [Theory]
    [InlineData("FAIL")]
    [InlineData("ERROR")]
    [InlineData("NOT-EARNED")]
    [InlineData("SKIPPED")]
    public void ACheckThatDoesNotPassCannotBeMadeWithoutAReason(string verdict) =>
        Assert.Throws<ArgumentException>(() => new CheckResult(CheckId.Green, CheckLevel.Required, CheckVerdictNames.Parse(verdict)!.Value, ReasonCode.None, "why"));

    [Theory]
    [InlineData("FAIL")]
    [InlineData("ERROR")]
    [InlineData("NOT-EARNED")]
    public void AGateStepThatDoesNotPassCannotBeMadeWithoutAReason(string verdict) =>
        Assert.Throws<ArgumentException>(() => new GateStep("build", 1, Enum.GetValues<Verdict>().Single(value => VerdictNames.ToContractName(value) == verdict), ReasonCode.None, 0, "detail"));

    [Fact]
    public void APassingOrNotApplicableResultNeedsNoReason()
    {
        Assert.Equal(ReasonCode.None, new CheckResult(CheckId.Green, CheckLevel.Required, CheckVerdict.Pass, ReasonCode.None, "ok").Reason);
        Assert.Equal(ReasonCode.None, new CheckResult(CheckId.Trace, CheckLevel.Off, CheckVerdict.NotApplicable, ReasonCode.None, "off").Reason);
        Assert.Equal(ReasonCode.None, new GateStep("format", 0, Verdict.NotApplicable, ReasonCode.None, 0, "no changed C# lines").Reason);
    }

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex ContractName();
}
