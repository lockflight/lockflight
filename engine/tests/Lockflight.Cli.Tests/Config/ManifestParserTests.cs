using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Config;

public sealed class ManifestParserTests
{
    private static ManifestLoadResult Parse(string yaml) => new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(yaml);

    [Fact]
    public void AValidManifestYieldsProtectedGlobsAndTheStopGate()
    {
        ManifestLoadResult result = Parse("""
            # Lockflight manifest
            version: 1
            protected:
              - "secrets/**"
              - '**/*.lock'   # lock files
            stop:
              gate: true
              target: samples/app
            """);

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal(["secrets/**", "**/*.lock"], result.Manifest!.ProtectedGlobs);
        Assert.True(result.Manifest.StopGate);
        Assert.Equal("samples/app", result.Manifest.StopTarget);
    }

    [Fact]
    public void TheReviewSectionSetsTheModelAndBudgetOfTheReviewOfRecord()
    {
        ManifestLoadResult result = Parse("version: 1\nreview:\n  model: sonnet\n  maxUsd: 1.50\n");

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal(new ReviewSettings("sonnet", 1.50m) { FromManifest = true }, result.Manifest!.Review);
    }

    [Fact]
    public void WithoutAReviewSectionTheDefaultsApply() =>
        Assert.Equal(ReviewSettings.Default, Parse("version: 1\n").Manifest!.Review);

    [Theory]
    [InlineData("review:\n  maxUsd: free\n", "'review.maxUsd'")]
    [InlineData("review:\n  maxUsd: 0\n", "'review.maxUsd'")]
    [InlineData("review:\n  model: \"opus; rm\"\n", "'review.model'")]
    [InlineData("review:\n  dimensions: all\n", "unknown key 'review.dimensions'")]
    [InlineData("review: opus\n", "'review' must be a mapping")]
    public void AnInvalidReviewSectionIsAnError(string yaml, string expected)
    {
        ManifestLoadResult result = Parse("version: 1\n" + yaml);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownKeyIsAnErrorPointingAtItsLine()
    {
        ManifestLoadResult result = Parse("version: 1\nprotected:\n  - a/**\ngats:\n  coverage: 80\n");

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Equal(4, result.Error!.Line);
        Assert.Contains("unknown key 'gats'", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongTypeIsAnErrorRatherThanADefault()
    {
        ManifestLoadResult result = Parse("stop:\n  gate: yes-please\n");

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains("'stop.gate' must be true or false", result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenYamlIsReportedWithAPosition()
    {
        ManifestLoadResult result = Parse("protected:\n  - \"secrets/**\n");

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.True(result.Error!.Line >= 2);
        Assert.StartsWith("/repo/lockflight.yaml:", result.Error.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGatesSectionSetsTheTestFrameworkAndTimeout()
    {
        ManifestLoadResult result = Parse("gates:\n  timeoutSeconds: 300\n  test:\n    framework: net10.0\n");

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal("net10.0", result.Manifest!.Gates.TestFramework);
        Assert.Equal(300, result.Manifest.Gates.TimeoutSeconds);
    }

    [Fact]
    public void CoverageDefaultsToOffAndReadsTheCollectorAndThresholds()
    {
        Assert.Equal(CoverageCollector.None, Parse("protected: []\n").Manifest!.Gates.Coverage.Collector);

        ManifestLoadResult result = Parse("gates:\n  coverage:\n    collector: coverlet\n    patchThreshold: 70\n    minChangedLines: 0\n");

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal(new CoverageSettings(CoverageCollector.Coverlet, 70, 0), result.Manifest!.Gates.Coverage);
    }

    [Theory]
    [InlineData("gates:\n  timeoutSeconds: 0\n", "gates.timeoutSeconds")]
    [InlineData("gates:\n  test:\n    framework: 10.0\n", "gates.test.framework")]
    [InlineData("gates:\n  coverage: 80\n", "'gates.coverage' must be a mapping")]
    [InlineData("gates:\n  coverage:\n    collector: dotcover\n", "gates.coverage.collector")]
    [InlineData("gates:\n  coverage:\n    patchThreshold: 101\n", "gates.coverage.patchThreshold")]
    [InlineData("gates:\n  coverage:\n    branch: 80\n", "unknown key 'gates.coverage.branch'")]
    public void InvalidGateSettingsAreRejectedWithTheKey(string yaml, string expectedFragment)
    {
        ManifestLoadResult result = Parse(yaml);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(expectedFragment, result.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[a, b]: 1\n")]
    [InlineData("{a: 1}: 2\n")]
    [InlineData("stop:\n  [gate]: true\n")]
    [InlineData("gates:\n  {x: 1}: 2\n")]
    [InlineData("gates:\n  coverage:\n    [collector]: none\n")]
    [InlineData("gates:\n  test:\n    ? [framework]\n    : net10.0\n")]
    [InlineData("- just\n- a list\n")]
    [InlineData("plain scalar")]
    [InlineData("protected: &a [*a]\n")]
    [InlineData("protected:\n  - [nested]\n")]
    [InlineData("version: 1\nversion: 1\n")]
    [InlineData("--- \n...\n--- {protected: 1}\n")]
    [InlineData("stop: !!binary aGVsbG8=\n")]
    [InlineData("\t- tab\n")]
    public void MalformedShapesAreManifestErrorsNotCrashes(string yaml)
    {
        ManifestLoadResult result = Parse(yaml);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.StartsWith("/repo/lockflight.yaml:", result.Error!.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void DiscoveryWalksUpToTheNearestManifest()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-manifest-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "protected:\n  - a/**\n");
            DirectoryInfo nested = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "deep"));

            ManifestLoadResult result = ManifestLocator.Discover(nested.FullName);

            Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
            Assert.Equal(root.FullName, result.Manifest!.Root);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void UnitsAreReadWithTheirBuildKindAndTestSettings()
    {
        ManifestLoadResult result = Parse("""
            version: 1
            units:
              - path: services/orders/
                build: Orders.sln
                test:
                  framework: net8.0
                  stopFilter: "Category!=Integration"
              - path: 'web\admin'
                kind: none
            """);

        Assert.Equal(ManifestLoadStatus.Loaded, result.Status);
        Assert.Equal(
            [new Unit("services/orders", "Orders.sln", UnitKind.Auto, "net8.0", "Category!=Integration"), new Unit("web/admin", Build: null, UnitKind.None, TestFramework: null, StopFilter: null)],
            result.Manifest!.Units);
    }

    [Fact]
    public void AManifestWithoutUnitsHasNone()
    {
        Assert.Empty(Parse("version: 1\n").Manifest!.Units);
    }

    [Theory]
    [InlineData("units:\n  - path: ../outside\n", "must be a relative path")]
    [InlineData("units:\n  - path: /abs\n", "must be a relative path")]
    [InlineData("units:\n  - path: C:/abs\n", "must be a relative path")]
    [InlineData("units:\n  - build: A.sln\n", "needs a 'path'")]
    [InlineData("units:\n  - path: a\n  - path: A\n", "is listed twice")]
    [InlineData("units:\n  - path: services\n  - path: services/orders\n", "are nested")]
    [InlineData("units:\n  - path: a\n    kind: java\n", "must be auto, dotnet or none")]
    [InlineData("units:\n  - path: a\n    owner: me\n", "unknown key 'units[].owner'")]
    [InlineData("units: services/orders\n", "must be a list of mappings")]
    public void AnInvalidUnitIsAnError(string yaml, string message)
    {
        ManifestLoadResult result = Parse(yaml);

        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        Assert.Contains(message, result.Error!.Message, StringComparison.Ordinal);
    }
}
