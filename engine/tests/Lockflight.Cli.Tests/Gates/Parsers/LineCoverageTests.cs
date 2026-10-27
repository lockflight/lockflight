using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class LineCoverageTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-cov-");

    [Fact]
    public void AbsoluteFilenamesAreReadOnceFromTheClassLevel()
    {
        string report = Write("ms.cobertura.xml", $"""
            <coverage><packages><package><classes>
              <class name="A" filename="{Path.Combine(projectRoot.FullName, "src", "A.cs")}">
                <methods><method name="Run"><lines><line number="10" hits="3" /></lines></method></methods>
                <lines><line number="10" hits="3" /><line number="11" hits="0" /></lines>
              </class>
            </classes></package></packages></coverage>
            """);
        LineCoverage coverage = new();

        coverage.AddCobertura(report, projectRoot.FullName);

        Assert.Equal(3, coverage.HitsAt("src/A.cs", 10));
        Assert.Equal(0, coverage.HitsAt("src/A.cs", 11));
        Assert.Null(coverage.HitsAt("src/A.cs", 12));
    }

    [Fact]
    public void RelativeFilenamesResolveAgainstSourcesAndReportsMergeByMaxHits()
    {
        string sources = Path.Combine(projectRoot.FullName, "src");
        string first = Write("a.cobertura.xml", Coverlet(sources, hits: 0));
        string second = Write("b.cobertura.xml", Coverlet(sources, hits: 2));
        LineCoverage coverage = new();

        coverage.AddCobertura(first, projectRoot.FullName);
        coverage.AddCobertura(second, projectRoot.FullName);

        Assert.True(coverage.Covers("src/B.cs"));
        Assert.False(coverage.CoversAnyUnder("tests"));
        Assert.Equal(2, coverage.HitsAt("src/B.cs", 5));
    }

    [Fact]
    public void DeterministicSourcePathsMapBackToTheRepositoryRoot()
    {
        // ContinuousIntegrationBuild maps the repository root to /_/ in the PDBs, and a report read from them names /_/ paths.
        string unit = Path.Combine(projectRoot.FullName, "services", "orders");
        string microsoft = Write("ms.cobertura.xml", """
            <coverage><packages><package><classes>
              <class name="A" filename="/_/src/A.cs"><methods /><lines><line number="4" hits="2" /></lines></class>
            </classes></package></packages></coverage>
            """);
        string coverlet = Write("coverlet.cobertura.xml", Coverlet("/_/src/", hits: 1));
        LineCoverage coverage = new();

        coverage.AddCobertura(microsoft, projectRoot.FullName, unit);
        coverage.AddCobertura(coverlet, projectRoot.FullName, unit);

        Assert.Equal(2, coverage.HitsAt("services/orders/src/A.cs", 4));
        Assert.Equal(1, coverage.HitsAt("services/orders/src/B.cs", 5));
    }

    [Fact]
    public void AnotherDeterministicSourceRootIsNotGuessed()
    {
        string report = Write("ms.cobertura.xml", """
            <coverage><packages><package><classes>
              <class name="A" filename="/_1/src/A.cs"><methods /><lines><line number="4" hits="2" /></lines></class>
            </classes></package></packages></coverage>
            """);
        LineCoverage coverage = new();

        coverage.AddCobertura(report, projectRoot.FullName, projectRoot.FullName);

        Assert.False(coverage.CoversAnyUnder(string.Empty));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(projectRoot);

    private static string Coverlet(string sources, int hits) => $"""
        <coverage><sources><source>{sources}</source></sources><packages><package><classes>
          <class name="B" filename="B.cs"><methods /><lines><line number="5" hits="{hits}" /></lines></class>
        </classes></package></packages></coverage>
        """;

    private string Write(string name, string content)
    {
        string path = Path.Combine(projectRoot.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }
}
