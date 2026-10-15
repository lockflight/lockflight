using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class FailedTestsTests
{
    private const string Output = """
        failed Parlot.Tests.CompileTests.ShouldParseDecimal(culture: "uk-UA") (12ms)
          Assert.Equal() Failure
        passed Parlot.Tests.CompileTests.ShouldParseInt (1ms)
        failed Parlot.SourceGenerator.Tests.StandalonePackageTests.Packaged_Generator_Runs (17s 814ms)
        failed Parlot.Tests.CompileTests.ShouldParseDecimal(culture: "uk-UA") (9ms)
        """;

    [Fact]
    public void FailedTestNamesAreFoundOnceEach() =>
        Assert.Equal(
            ["Parlot.Tests.CompileTests.ShouldParseDecimal(culture: \"uk-UA\")", "Parlot.SourceGenerator.Tests.StandalonePackageTests.Packaged_Generator_Runs"],
            FailedTests.Find(Output));

    [Fact]
    public void IndentedLinesThatLookLikeResultsAreNotCounted()
    {
        const string output = "failed A.B.C (3ms)\n  Assert.Equal() Failure\n    failed A.B.D (1ms)\n  failed because (reasons)\nfailed A.B.E (1m 2s 5ms)\r\n";

        Assert.Equal(["A.B.C", "A.B.E"], FailedTests.Find(output));
        Assert.Equal(2, FailedTests.CountFailures(output));
    }

    [Fact]
    public void ALongListIsCutToAFewNamesAndACount()
    {
        string description = FailedTests.Describe([.. Enumerable.Range(1, 8).Select(index => $"T{index}")]);

        Assert.Equal("8 failed: T1, T2, T3, T4, T5 and 3 more", description);
    }
}
