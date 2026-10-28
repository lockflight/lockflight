using System.Xml.Linq;
using Lockflight.Cli.Quality;

namespace Lockflight.Cli.Tests.Quality;

public sealed class QualityPackTests
{
    [Fact]
    public void ANewEditorConfigIsTheRootAndMakesTheMetricsRulesWarnings()
    {
        string editorConfig = QualityPack.EditorConfig(null)!;

        Assert.StartsWith("root = true\n", editorConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1502.severity = warning\n", editorConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1505.severity = warning\n", editorConfig, StringComparison.Ordinal);
        Assert.Contains("dotnet_diagnostic.CA1506.severity = warning\n", editorConfig, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingEditorConfigKeepsItsContentAndGetsTheSectionLast()
    {
        const string team = "root = true\n[*.cs]\nindent_size = 4";

        string editorConfig = QualityPack.EditorConfig(team)!;

        Assert.StartsWith(team + "\n\n", editorConfig, StringComparison.Ordinal);
        Assert.Contains(QualityPack.Marker, editorConfig, StringComparison.Ordinal);
        Assert.Null(QualityPack.EditorConfig(editorConfig));
    }

    [Fact]
    public void ANewBuildPropsSetsTheRecommendedAnalysisLevelAndTheMetricsFile()
    {
        XDocument props = XDocument.Parse(QualityPack.BuildProps(null)!);

        Assert.Equal("latest-recommended", props.Descendants("AnalysisLevel").Single().Value);
        Assert.Equal("$(MSBuildThisFileDirectory)CodeMetricsConfig.txt", props.Descendants("AdditionalFiles").Single().Attribute("Include")!.Value);
    }

    [Fact]
    public void AnExistingBuildPropsGetsOnlyWhatItLacksBeforeItsEnd()
    {
        const string team = "<Project>\n  <PropertyGroup>\n    <AnalysisLevel>9.0-all</AnalysisLevel>\n  </PropertyGroup>\n</Project>\n";

        string proposed = QualityPack.BuildProps(team)!;

        XDocument props = XDocument.Parse(proposed);
        Assert.Equal("9.0-all", props.Descendants("AnalysisLevel").Single().Value);
        Assert.Single(props.Descendants("AdditionalFiles"));
        Assert.EndsWith("</Project>\n", proposed, StringComparison.Ordinal);
        Assert.Null(QualityPack.BuildProps(proposed));
    }

    [Fact]
    public void ABuildPropsWithoutAProjectEndGetsNoProposal() =>
        Assert.Null(QualityPack.BuildProps("<!-- empty -->"));

    [Fact]
    public void TheMetricsThresholdsAreWrittenOnlyWhenTheFileIsMissing()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-quality-");
        try
        {
            QualityFile metrics = QualityPack.Files(root.FullName).Single(file => file.RelativePath == QualityPack.CodeMetricsPath);
            Assert.Contains("CA1502(Method): 15", metrics.Content, StringComparison.Ordinal);
            Assert.False(metrics.Existing);

            File.WriteAllText(Path.Combine(root.FullName, QualityPack.CodeMetricsPath), "CA1502: 30\n");
            Assert.DoesNotContain(QualityPack.Files(root.FullName), file => file.RelativePath == QualityPack.CodeMetricsPath);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
