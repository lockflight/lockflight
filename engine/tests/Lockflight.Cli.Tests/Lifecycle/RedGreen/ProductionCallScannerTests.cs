using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class ProductionCallScannerTests
{
    private const string Samples = "Lockflight.Cli.Tests.Lifecycle.RedGreen.ProductionCallSamples";

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
        using ProductionCallScanner scanner = Scanner([Engine]);
        Assert.True(scanner.CallsProduction(className, method));
    }

    [Theory]
    [InlineData(nameof(ProductionCallSamples.AssertsOnConstants))]
    [InlineData(nameof(ProductionCallSamples.FailsUnconditionally))]
    [InlineData(nameof(ProductionCallSamples.NamesAProductionTypeOnly))]
    [InlineData(nameof(ProductionCallSamples.AwaitsWithoutProductionCode))]
    [InlineData("NoSuchMethod")]
    public void ATestThatOnlyAssertsDoesNotCallProductionCode(string method)
    {
        using ProductionCallScanner scanner = Scanner([Engine]);
        Assert.False(scanner.CallsProduction(Samples, method));
    }

    [Fact]
    public void OnlyTheGivenAssembliesCountAsProduction()
    {
        using ProductionCallScanner scanner = Scanner(["Some.Other.Assembly"]);
        Assert.False(scanner.CallsProduction(Samples, nameof(ProductionCallSamples.CallsDirectly)));
    }

    [Fact]
    public void AnUnknownClassDoesNotCallProductionCode()
    {
        using ProductionCallScanner scanner = Scanner([Engine]);
        Assert.False(scanner.CallsProduction("No.Such.Class", "M"));
    }

    private static ProductionCallScanner Scanner(string[] production) =>
        new(typeof(ProductionCallSamples).Assembly.Location, new HashSet<string>(production, StringComparer.OrdinalIgnoreCase));
}
