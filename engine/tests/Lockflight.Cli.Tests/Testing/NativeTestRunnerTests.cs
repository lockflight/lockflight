using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class NativeTestRunnerTests
{
    [Fact]
    public void XunitXmlYieldsOutcomesExceptionsAndTopFrames()
    {
        const string xml = """
            <assemblies><assembly><collection>
              <test name="Shop.Tests.ProbeTests.Stub" type="Shop.Tests.ProbeTests" method="Stub" result="Fail">
                <failure exception-type="System.NotImplementedException"><message>x</message>
                  <stack-trace>at Shop.Api.PriceCalculator.ShippingCost(Decimal weightKg) in /src/PriceCalculator.cs:line 22
               at Shop.Tests.ProbeTests.Stub() in /tests/ProbeTests.cs:line 9</stack-trace></failure>
              </test>
              <test name="Shop.Tests.ProbeTests.Rows(value: 1)" type="Shop.Tests.ProbeTests" method="Rows" result="Pass" />
              <test name="Shop.Tests.ProbeTests.Skipped" type="Shop.Tests.ProbeTests" method="Skipped" result="Skip" />
            </collection></assembly></assemblies>
            """;

        IReadOnlyList<ExecutedTest> results = NativeTestRunner.Parse("Shop.Tests", xml);

        Assert.Equal("Shop.Tests:Shop.Tests.ProbeTests.Stub", results[0].Key);
        Assert.Equal(TestOutcome.Fail, results[0].Outcome);
        Assert.Equal("System.NotImplementedException", results[0].ExceptionType);
        Assert.Equal("Shop.Api.PriceCalculator.ShippingCost(Decimal weightKg)", results[0].TopFrame);
        Assert.Equal("Shop.Tests:Shop.Tests.ProbeTests.Rows", results[1].Key);
        Assert.Equal(TestOutcome.Skip, results[2].Outcome);
    }
}
