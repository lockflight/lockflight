using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TrxTestResultsTests
{
    // The shape of a real NUnit TRX through VSTest, trimmed to what the parser reads: a parameterized fixture's arguments in the
    // class, a test case's in the method.
    private const string NUnitTrx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="t1" testName="Costs(1,2)" outcome="Passed" />
            <UnitTestResult testId="t2" testName="Costs(2,2)" outcome="Failed">
              <Output><ErrorInfo>
                <Message>  Assert.That(x, Is.EqualTo(5))
          Expected: 5
          But was:  4
        </Message>
                <StackTrace>   at Shop.Tests.Prices.Costs(Int32 a, Int32 b) in /src/Shop.Tests/Prices.cs:line 9</StackTrace>
              </ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="t3" testName="Tracks" outcome="Passed" />
            <UnitTestResult testId="t4" testName="Tracks" outcome="NotExecuted"><Output><ErrorInfo><Message>no Docker here</Message></ErrorInfo></Output></UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest id="t1"><TestMethod className="Shop.Tests.Prices" name="Costs(1,2)" /></UnitTest>
            <UnitTest id="t2"><TestMethod className="Shop.Tests.Prices" name="Costs(2,2)" /></UnitTest>
            <UnitTest id="t3"><TestMethod className="Shop.Tests.Tracking(&quot;A&quot;)" name="Tracks" /></UnitTest>
            <UnitTest id="t4"><TestMethod className="Shop.Tests.Box&lt;Int32&gt;" name="Tracks" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    [Fact]
    public void NUnitRowsAndFixtureInstancesKeyToTheirMethod()
    {
        IReadOnlyList<ExecutedTest> results = TrxTestResults.Parse("Shop.Tests", NUnitTrx, TrxFailureMessages.NUnit);

        Assert.Equal(
            ["Shop.Tests:Shop.Tests.Prices.Costs", "Shop.Tests:Shop.Tests.Prices.Costs", "Shop.Tests:Shop.Tests.Tracking.Tracks", "Shop.Tests:Shop.Tests.Box.Tracks"],
            results.Select(result => result.Key));
        Assert.Equal(("Costs(2,2)", TrxFailureMessages.NUnitAssertion, "Shop.Tests.Prices.Costs(Int32 a, Int32 b)"), (results[1].Name, results[1].ExceptionType, results[1].TopFrame));
        Assert.Equal(TestOutcome.Skip, results[3].Outcome);
        Assert.Equal("no Docker here", results[3].SkipReason);
    }

    [Theory]
    [InlineData("Costs", "Costs")]
    [InlineData("Costs(1,2)", "Costs")]
    [InlineData("Shop.Tests.Outer+Inner", "Shop.Tests.Outer+Inner")]
    [InlineData("Shop.Tests.Box<Int32>", "Shop.Tests.Box")]
    public void ArgumentsAreDroppedFromNames(string name, string expected) => Assert.Equal(expected, TrxTestResults.WithoutArguments(name));
}
