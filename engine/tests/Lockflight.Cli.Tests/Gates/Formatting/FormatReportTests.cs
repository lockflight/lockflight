using Lockflight.Cli.Gates.Formatting;

namespace Lockflight.Cli.Tests.Gates.Formatting;

public sealed class FormatReportTests
{
    [Fact]
    public void EveryChangeOfEveryFileIsReadWithAPathRelativeToTheRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");
        string file = Path.Combine(root, "src", "B.cs").Replace("\\", "\\\\", StringComparison.Ordinal);
        string json = $$"""
            [
              { "FilePath": "{{file}}", "FileChanges": [
                { "LineNumber": 4, "CharNumber": 2, "DiagnosticId": "WHITESPACE", "FormatDescription": "Fix whitespace formatting. Replace 3 characters with '\\r\\n\\s\\s\\s\\s'." } ] },
              { "FilePath": "{{file}}", "FileChanges": [
                { "LineNumber": 1, "CharNumber": 13, "DiagnosticId": "ENDOFLINE", "FormatDescription": "Fix end of line marker. Replace 1 characters with '\\r\\n'." } ] }
            ]
            """;

        IReadOnlyList<FormatDiagnostic>? diagnostics = FormatReport.Parse(json, root);

        Assert.NotNull(diagnostics);
        Assert.Equal(["src/B.cs:4:WHITESPACE", "src/B.cs:1:ENDOFLINE"], diagnostics.Select(diagnostic => $"{diagnostic.File}:{diagnostic.Line}:{diagnostic.DiagnosticId}"));
        Assert.Equal("Fix whitespace formatting. Replace 3 characters with '\\r\\n\\s\\s\\s\\s'.", diagnostics[0].Description);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[ { "FilePath": "a.cs" } ]""")]
    [InlineData("""[ { "FilePath": "a.cs", "FileChanges": [ { "DiagnosticId": "WHITESPACE" } ] } ]""")]
    public void AReportInAnUnexpectedShapeIsNotRead(string json) =>
        Assert.Null(FormatReport.Parse(json, Path.GetTempPath()));
}
