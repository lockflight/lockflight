using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class BuildDiagnosticsTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-diag-");

    private string Output => $"""
        {projectRoot.FullName}/src/A.cs(10,13): warning CS0168: The variable 'x' is declared but never used [{projectRoot.FullName}/src/A.csproj]
        {projectRoot.FullName}/src/B.cs(3,1): warning CA1822: Member 'Run' does not access instance data [{projectRoot.FullName}/src/A.csproj]
        Build succeeded.
        {projectRoot.FullName}/src/A.cs(10,13): warning CS0168: The variable 'x' is declared but never used [{projectRoot.FullName}/src/A.csproj]
        """;

    [Fact]
    public void WarningsAreParsedOnceWithPathsRelativeToTheRoot()
    {
        IReadOnlyList<BuildDiagnostic> diagnostics = BuildDiagnostics.Parse(Output, projectRoot.FullName);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal("CS0168|src/A.cs|The variable 'x' is declared but never used", diagnostics[0].Fingerprint);
    }

    [Fact]
    public void OnlyWarningsBeyondTheBaselineAreNew()
    {
        DiagnosticsBaseline baseline = new(projectRoot.FullName, unitPath: null);
        baseline.Save(BuildDiagnostics.Parse(Output, projectRoot.FullName));
        List<BuildDiagnostic> later =
        [
            .. BuildDiagnostics.Parse(Output, projectRoot.FullName),
            new BuildDiagnostic("CS0168", "src/A.cs", 42, "The variable 'x' is declared but never used"),
        ];

        IReadOnlyList<BuildDiagnostic> fresh = baseline.NewIn(later);

        BuildDiagnostic only = Assert.Single(fresh);
        Assert.Equal(42, only.Line);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
