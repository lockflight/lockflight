using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RedClassifierTests
{
    private static readonly HashSet<string> TestClasses = ["Shop.Tests.ShippingTests", "Shop.Tests.Outer+Inner"];

    private static readonly string[] TestDirectories = ["/repo/tests/Shop.Tests"];

    [Theory]
    [InlineData("System.NotImplementedException", "Shop.Api.PriceCalculator.ShippingCost(Decimal w)", "RED_STUB")]
    [InlineData("Xunit.Sdk.EqualException", "Shop.Tests.ShippingTests.Costs()", "RED_ASSERT")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.Cheat()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.Helpers.Throw()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.Outer.Inner.M()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.<>c.<Cheat>b__1_0()", "RED_IN_TEST")]
    [InlineData("System.NullReferenceException", "Shop.Api.PriceCalculator.ShippingCost(Decimal w)", "RED_OTHER")]
    public void FailuresAreJudgedByExceptionAndWhereItWasThrown(string exception, string topFrame, string expected) =>
        Assert.Equal(RedClassNames.Parse(expected), RedClassifier.Classify([Fail(exception, topFrame, file: null)], TestClasses, TestDirectories));

    [Fact]
    public void AHelperClassWithoutTestsIsRecognisedByItsFile() =>
        Assert.Equal(
            RedClass.RedInTest,
            RedClassifier.Classify([Fail("System.NotImplementedException", "Shop.Tests.Support.Throwers.Throw()", "/repo/tests/Shop.Tests/Support/Throwers.cs")], TestClasses, TestDirectories));

    [Fact]
    public void ATheoryRowThatPassesMakesTheWholeTestGreenBefore() =>
        Assert.Equal(RedClass.GreenBefore, RedClassifier.Classify([Fail("Xunit.Sdk.EqualException", "x.Y()", file: null), Row(TestOutcome.Pass)], TestClasses, TestDirectories));

    [Fact]
    public void SkippedOrMissingResultsAreRejected()
    {
        Assert.Equal(RedClass.Skipped, RedClassifier.Classify([Row(TestOutcome.Skip)], TestClasses, TestDirectories));
        Assert.Equal(RedClass.Skipped, RedClassifier.Classify([], TestClasses, TestDirectories));
    }

    private static ExecutedTest Fail(string exception, string topFrame, string? file) => new("a:k", "n", TestOutcome.Fail, exception, topFrame, file);

    private static ExecutedTest Row(TestOutcome outcome) => new("a:k", "n", outcome, null, null);
}
