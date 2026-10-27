using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RedClassifierTests
{
    private static readonly HashSet<string> TestClasses = ["Shop.Tests.ShippingTests", "Shop.Tests.Outer+Inner"];

    private static readonly string[] TestDirectories = ["/repo/tests/Shop.Tests"];

    private static readonly ProductionCode Production = new(
        ["/repo/src/Shop.Api"], new HashSet<string>(["Shop.Api"]), new HashSet<string>(["Shop.Api.PriceCalculator", "Shop.Api.Orders.Line"]));

    [Theory]
    [InlineData("System.NotImplementedException", "Shop.Api.PriceCalculator.ShippingCost(Decimal w)", "RED_STUB")]
    [InlineData("System.NotImplementedException", "Shop.Api.Orders.Line.<Total>g__Sum|0_0()", "RED_STUB")]
    [InlineData("Xunit.Sdk.EqualException", "Shop.Tests.ShippingTests.Costs()", "RED_ASSERT")]
    [InlineData("Xunit.Sdk.ThrowsException", "Shop.Tests.ShippingTests.Costs()", "RED_ASSERT")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.Cheat()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.Helpers.Throw()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.Outer.Inner.M()", "RED_IN_TEST")]
    [InlineData("System.NotImplementedException", "Shop.Tests.ShippingTests.<>c.<Cheat>b__1_0()", "RED_IN_TEST")]
    [InlineData("Xunit.Sdk.FailException", "Shop.Tests.ShippingTests.Costs()", "RED_IN_TEST")]
    [InlineData("Xunit.Sdk.TestPipelineException", "Shop.Tests.Fixtures.Database..ctor()", "RED_IN_TEST")]
    [InlineData("Xunit.Sdk.TestClassException", "Shop.Api.PriceCalculator..ctor()", "RED_IN_TEST")]
    [InlineData("System.NullReferenceException", "Shop.Api.PriceCalculator.ShippingCost(Decimal w)", "RED_OTHER")]
    [InlineData("System.InvalidOperationException", "System.Linq.ThrowHelper.ThrowNoElementsException()", "RED_OTHER")]
    [InlineData("System.NotImplementedException", "Some.Library.Client.Send()", "RED_OTHER")]
    public void FailuresAreJudgedByExceptionAndWhereItWasThrown(string exception, string topFrame, string expected) =>
        Assert.Equal(RedClassNames.Parse(expected), Classify(Fail(exception, topFrame, file: null), callsProduction: true));

    /// <summary>Exception types and top frames as the libraries report them in a real xUnit v3 run (the native report) and a VSTest run (TRX).</summary>
    [Theory]
    [InlineData(VSTestRunner.AssertionException, "Shop.Tests.ShippingTests.Costs()")]
    [InlineData("Shouldly.ShouldAssertException", "Shop.Tests.ShippingTests.Costs()")]
    [InlineData("Shouldly.ShouldMatchApprovedException", "Shop.Tests.ShippingTests.Costs()")]
    [InlineData("Shouldly.ShouldCompleteInException", "Shop.Tests.ShippingTests.Costs()")]
    [InlineData("Xunit.Sdk.XunitException", "FluentAssertions.Execution.LateBoundTestFramework.Throw(String message)")]
    [InlineData(null, "FluentAssertions.Execution.LateBoundTestFramework.Throw(String message)")]
    [InlineData("FluentAssertions.Execution.AssertionFailedException", "FluentAssertions.Execution.FallbackTestFramework.Throw(String message)")]
    [InlineData("Xunit.Sdk.XunitException", "AwesomeAssertions.Execution.LateBoundTestFramework.Throw(String message)")]
    [InlineData(null, "AwesomeAssertions.Execution.LateBoundTestFramework.Throw(String message)")]
    [InlineData("AwesomeAssertions.Execution.AssertionFailedException", "AwesomeAssertions.Execution.FallbackTestFramework.Throw(String message)")]
    [InlineData("NSubstitute.Exceptions.ReceivedCallsException", "NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification)")]
    [InlineData("NSubstitute.Exceptions.CallSequenceNotFoundException", "NSubstitute.Core.SequenceChecking.SequenceInOrderAssertion.Assert(IQueryResults queryResult)")]
    [InlineData("Moq.MockException", "Moq.Mock.Verify(Mock mock, LambdaExpression expression, Times times, String failMessage)")]
    [InlineData("VerifyException", null)]
    public void AssertionLibraryFailuresInATestThatCallsProductionCodeAreAssertionReds(string? exception, string? topFrame)
    {
        Assert.Equal(RedClass.RedAssert, Classify(Fail(exception, topFrame, file: null), callsProduction: true));
        Assert.Equal(RedClass.RedNoProduction, Classify(Fail(exception, topFrame, file: null), callsProduction: false));
    }

    [Theory]
    [InlineData("NSubstitute.Exceptions.CouldNotSetReturnDueToNoLastCallException", "NSubstitute.Core.ConfigureCall.SetResultForLastCall(IReturn valueToReturn)", "RED_OTHER")]
    [InlineData(null, "Shop.Tests.ShippingTests.Costs()", "RED_IN_TEST")]
    [InlineData("VerifyCheckException", "VerifyTests.VerifyChecks.Run()", "RED_OTHER")]
    public void MisusingAnAssertionLibraryIsNotAnAssertionRed(string? exception, string topFrame, string expected) =>
        Assert.Equal(RedClassNames.Parse(expected), Classify(Fail(exception, topFrame, file: null), callsProduction: true));

    [Fact]
    public void AnAssertionInATestThatNeverCallsProductionCodeIsRejected() =>
        Assert.Equal(RedClass.RedNoProduction, Classify(Fail("Xunit.Sdk.EqualException", "Shop.Tests.ShippingTests.Costs()", file: null), callsProduction: false));

    [Fact]
    public void AStubIsRecognisedByItsProductionFileWithoutItsType() =>
        Assert.Equal(
            RedClass.RedStub,
            Classify(Fail("System.NotImplementedException", "Shop.Api.Generated.Stub()", "/repo/src/Shop.Api/Generated/Stub.cs"), callsProduction: true));

    [Fact]
    public void AHelperClassWithoutTestsIsRecognisedByItsFile() =>
        Assert.Equal(
            RedClass.RedInTest,
            Classify(Fail("System.NotImplementedException", "Shop.Tests.Support.Throwers.Throw()", "/repo/tests/Shop.Tests/Support/Throwers.cs"), callsProduction: true));

    [Fact]
    public void WithoutKnownProductionCodeNoStubIsAccepted() =>
        Assert.Equal(
            RedClass.RedOther,
            RedClassifier.Classify([Fail("System.NotImplementedException", "Shop.Api.PriceCalculator.ShippingCost(Decimal w)", file: null)], TestClasses, TestDirectories, ProductionCode.None, callsProduction: false));

    [Fact]
    public void ATheoryRowThatPassesMakesTheWholeTestGreenBefore() =>
        Assert.Equal(RedClass.GreenBefore, RedClassifier.Classify([Fail("Xunit.Sdk.EqualException", "x.Y()", file: null), Row(TestOutcome.Pass)], TestClasses, TestDirectories, Production, callsProduction: true));

    [Fact]
    public void SkippedOrMissingResultsAreRejected()
    {
        Assert.Equal(RedClass.Skipped, RedClassifier.Classify([Row(TestOutcome.Skip)], TestClasses, TestDirectories, Production, callsProduction: true));
        Assert.Equal(RedClass.Skipped, RedClassifier.Classify([], TestClasses, TestDirectories, Production, callsProduction: true));
    }

    [Fact]
    public void OnlyStubAndAssertionRedsAreAccepted() =>
        Assert.Equal([RedClass.RedStub, RedClass.RedAssert], Enum.GetValues<RedClass>().Where(RedClassNames.IsAccepted));

    [Fact]
    public void EveryRejectedClassSaysWhatToChange() =>
        Assert.All(Enum.GetValues<RedClass>().Where(redClass => !RedClassNames.IsAccepted(redClass)), redClass => Assert.NotEmpty(RedClassFixes.For(redClass)));

    private static RedClass Classify(ExecutedTest row, bool callsProduction) => RedClassifier.Classify([row], TestClasses, TestDirectories, Production, callsProduction);

    private static ExecutedTest Fail(string? exception, string? topFrame, string? file) => new("a:k", "n", TestOutcome.Fail, exception, topFrame, file);

    private static ExecutedTest Row(TestOutcome outcome) => new("a:k", "n", outcome, null, null);
}
