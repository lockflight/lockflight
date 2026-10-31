using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class TestCodeScannerTests
{
    private const string Samples = "Lockflight.Cli.Tests.Lifecycle.RedGreen.ProductionCallSamples";

    private const string AssertionSampleClass = "Lockflight.Cli.Tests.Lifecycle.RedGreen.AssertionSamples";

    private static readonly string Engine = typeof(RedClass).Assembly.GetName().Name!;

    [Theory]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsDirectly))]
    [InlineData(Samples, nameof(ProductionCallSamples.ConstructsAProductionType))]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsThroughALambda))]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsThroughAHelper))]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsThroughALocalFunction))]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsAfterAnAwait))]
    [InlineData(Samples, nameof(ProductionCallSamples.CallsAProductionGenericInstance))]
    [InlineData(Samples + "+Nested", nameof(ProductionCallSamples.Nested.CallsDirectly))]
    public void ATestReachingProductionCodeCallsIt(string className, string method)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.True(scanner.CallsProduction(className, method));
    }

    [Theory]
    [InlineData(nameof(ProductionCallSamples.CallsDirectly), "RedClassNames.ToName()")]
    [InlineData(nameof(ProductionCallSamples.ConstructsAProductionType), "new FlakyCheckResult()")]
    [InlineData(nameof(ProductionCallSamples.ReadsAProductionProperty), "FlakyCheckResult.BudgetExhausted")]
    public void TheFirstProductionMemberReachedIsNamed(string method, string expected)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.Equal(expected, scanner.ProductionCall(Samples, method));
    }

    [Fact]
    public void ATestInheritedFromAGenericBaseCallsTheProductionCodeTheBaseCalls()
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.True(scanner.CallsProduction("Lockflight.Cli.Tests.Lifecycle.RedGreen.InheritedProductionCallSample", nameof(InheritedProductionCallSample.CallsFromAGenericBase)));
    }

    [Fact]
    public void ATypeArgumentAloneIsNoProductionCall()
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.Null(scanner.ProductionCall(Samples, nameof(ProductionCallSamples.NamesAProductionTypeAsATypeArgument)));
    }

    [Theory]
    [InlineData(nameof(ProductionCallSamples.AssertsOnConstants))]
    [InlineData(nameof(ProductionCallSamples.FailsUnconditionally))]
    [InlineData(nameof(ProductionCallSamples.NamesAProductionTypeOnly))]
    [InlineData(nameof(ProductionCallSamples.AwaitsWithoutProductionCode))]
    [InlineData("NoSuchMethod")]
    public void ATestThatOnlyAssertsDoesNotCallProductionCode(string method)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.False(scanner.CallsProduction(Samples, method));
    }

    [Fact]
    public void OnlyTheGivenAssembliesCountAsProduction()
    {
        using TestCodeScanner scanner = Scanner(["Some.Other.Assembly"]);
        Assert.False(scanner.CallsProduction(Samples, nameof(ProductionCallSamples.CallsDirectly)));
    }

    [Fact]
    public void AnUnknownClassDoesNotCallProductionCode()
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.False(scanner.CallsProduction("No.Such.Class", "M"));
    }

    [Theory]
    [InlineData(nameof(AssertionSamples.AssertsWithXunit))]
    [InlineData(nameof(AssertionSamples.AssertsInAHelper))]
    [InlineData(nameof(AssertionSamples.AssertsThroughALambda))]
    [InlineData(nameof(AssertionSamples.AssertsAfterAnAwait))]
    [InlineData(nameof(AssertionSamples.AssertsWithAnAssertMethodOfAnotherLibrary))]
    [InlineData(nameof(AssertionSamples.ThrowsWhenTheResultIsWrong))]
    public void ATestThatAssertsOrThrowsIsNotAssertionFree(string method)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.False(scanner.AssertsNothing(AssertionSampleClass, method));
    }

    [Theory]
    [InlineData(nameof(AssertionSamples.CallsProductionWithoutAsserting))]
    [InlineData(nameof(AssertionSamples.DoesNothing))]
    [InlineData(nameof(AssertionSamples.AwaitsWithoutAsserting))]
    public void ATestWhoseCodeNeitherAssertsNorThrowsIsAssertionFree(string method)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.True(scanner.AssertsNothing(AssertionSampleClass, method));
    }

    [Theory]
    [InlineData(AssertionSampleClass, "NoSuchMethod")]
    [InlineData("No.Such.Class", "M")]
    public void ATestThatCannotBeFoundIsNotCalledAssertionFree(string className, string method)
    {
        using TestCodeScanner scanner = Scanner([Engine]);
        Assert.False(scanner.AssertsNothing(className, method));
    }

    private static TestCodeScanner Scanner(string[] production) =>
        new(typeof(ProductionCallSamples).Assembly.Location, new HashSet<string>(production, StringComparer.OrdinalIgnoreCase));
}
