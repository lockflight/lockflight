using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class VSTestRunnerTests
{
    // The shape of a real xUnit v2 TRX on net8, trimmed to what the parser reads.
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="t1" testName="App.Tests.CalcTests.DividesNumbers" outcome="Failed">
              <Output><ErrorInfo>
                <Message>System.NotImplementedException : The method or operation is not implemented.</Message>
                <StackTrace>   at App.Calc.Div(Int32 a, Int32 b) in /src/App/Calc.cs:line 5
           at App.Tests.CalcTests.DividesNumbers() in /src/App.Tests/CalcTests.cs:line 6</StackTrace>
              </ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="t2" testName="App.Tests.CalcTests.AddsTwoNumbers" outcome="Failed">
              <Output><ErrorInfo>
                <Message>Assert.Equal() Failure: Values differ</Message>
                <StackTrace>   at App.Tests.CalcTests.AddsTwoNumbers() in /src/App.Tests/CalcTests.cs:line 5</StackTrace>
              </ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="t3" testName="App.Tests.CalcTests.Theory(x: 1)" outcome="Passed" />
            <UnitTestResult testId="t5" testName="App.Tests.CalcTests.FixtureFails" outcome="Failed">
              <Output><ErrorInfo><Message>Class fixture type 'App.Tests.Db' threw in its constructor</Message></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="t6" testName="App.Tests.CalcTests.MessageLooksTyped" outcome="Failed">
              <Output><ErrorInfo><Message>Order.Total : x</Message></ErrorInfo></Output>
            </UnitTestResult>
            <UnitTestResult testId="t4" testName="App.Tests.CalcTests+Nested.Inner" outcome="NotExecuted" />
          </Results>
          <TestDefinitions>
            <UnitTest id="t1"><TestMethod className="App.Tests.CalcTests" name="DividesNumbers" /></UnitTest>
            <UnitTest id="t2"><TestMethod className="App.Tests.CalcTests" name="AddsTwoNumbers" /></UnitTest>
            <UnitTest id="t3"><TestMethod className="App.Tests.CalcTests" name="Theory" /></UnitTest>
            <UnitTest id="t4"><TestMethod className="App.Tests.CalcTests+Nested" name="Inner" /></UnitTest>
            <UnitTest id="t5"><TestMethod className="App.Tests.CalcTests" name="FixtureFails" /></UnitTest>
            <UnitTest id="t6"><TestMethod className="App.Tests.CalcTests" name="MessageLooksTyped" /></UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    [Fact]
    public void FailuresCarryTheirExceptionTypeAndTopFrameAndRowsKeyByMethod()
    {
        IReadOnlyList<ExecutedTest> results = VSTestRunner.Parse("App.Tests", Trx);

        Assert.Equal(new ExecutedTest("App.Tests:App.Tests.CalcTests.DividesNumbers", "App.Tests.CalcTests.DividesNumbers", TestOutcome.Fail,
            "System.NotImplementedException", "App.Calc.Div(Int32 a, Int32 b)", "/src/App/Calc.cs") { TopFrameLine = 5 }, results[0]);
        Assert.Equal(VSTestRunner.AssertionException, results[1].ExceptionType);
        Assert.Equal(("App.Tests:App.Tests.CalcTests.Theory", TestOutcome.Pass), (results[2].Key, results[2].Outcome));
        Assert.Equal(("App.Tests:App.Tests.CalcTests+Nested.Inner", TestOutcome.Skip), (results[5].Key, results[5].Outcome));
        Assert.Null(results[3].ExceptionType);
        Assert.Null(results[4].ExceptionType);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Aborted")]
    [InlineData("Error")]
    public void AResultThatTimedOutWasAbortedOrErroredIsAFailureNotATestThatDidNotRun(string outcome)
    {
        const string template = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results><UnitTestResult testId="t1" testName="App.Tests.CalcTests.Slow" outcome="OUTCOME" /></Results>
              <TestDefinitions><UnitTest id="t1"><TestMethod className="App.Tests.CalcTests" name="Slow" /></UnitTest></TestDefinitions>
            </TestRun>
            """;

        ExecutedTest result = Assert.Single(VSTestRunner.Parse("App.Tests", template.Replace("OUTCOME", outcome, StringComparison.Ordinal)));

        Assert.Equal(TestOutcome.Fail, result.Outcome);
        Assert.Null(result.ExceptionType);
    }

    [Fact]
    public void FilterValuesEscapeVSTestsReservedCharacters()
    {
        Assert.Equal(@"Ns.Outer+Inner.M\(x\)\,\=\!\~\&\|\\", VSTestRunner.Escape(@"Ns.Outer+Inner.M(x),=!~&|\"));
    }

    [Fact]
    public void AssemblyMetadataListsFactsWithTheirSkipReasonAndRequirementTraits()
    {
        IReadOnlyList<TestCase> tests = AssemblyTestMetadata.Read(typeof(VSTestRunnerTests).Assembly.Location);

        TestCase self = Assert.Single(tests, test => test.Method == nameof(AssemblyMetadataListsFactsWithTheirSkipReasonAndRequirementTraits));
        Assert.Equal("Lockflight.Cli.Tests.Testing.VSTestRunnerTests", self.Class);
        TestCase traced = Assert.Single(tests, test => test.Method == nameof(Sample.Traced));
        Assert.Equal("Lockflight.Cli.Tests.Testing.VSTestRunnerTests+Sample", traced.Class);
        Assert.Equal(["FR-900"], traced.Requirements);
        Assert.Equal("kept off", Assert.Single(tests, test => test.Method == nameof(Sample.Skipped)).SkipReason);
        Assert.DoesNotContain(tests, test => test.Method == nameof(Hidden.InAnInternalClass));
    }

    // A non-public test class on purpose: the metadata reader must not list what xUnit would not run.
#pragma warning disable xUnit1000
    /// <summary>A test in a class xUnit v2 would not run (not public): never listed.</summary>
    internal sealed class Hidden
    {
        [Fact]
        public void InAnInternalClass() => Assert.True(true);
    }
#pragma warning restore xUnit1000

    /// <summary>Test methods that exist only to be read from metadata.</summary>
    public sealed class Sample
    {
        [Fact]
        [Trait("Requirement", "FR-900")]
        public void Traced() => Assert.True(true);

        [Fact(Skip = "kept off")]
        public void Skipped() => Assert.True(false);
    }
}
