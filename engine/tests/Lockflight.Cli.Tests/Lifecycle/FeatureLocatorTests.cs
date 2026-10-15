using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class FeatureLocatorTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-feature-");

    [Fact]
    public void TheOnlyFeatureIsChosenWhenSpecKitNamesNone()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "20261011-120000-shipping"));

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null);

        Assert.Null(lookup.Error);
        Feature feature = Assert.IsType<Feature>(lookup.Feature);
        Assert.Equal("20261011-120000-shipping", feature.Id);
        Assert.Equal(Path.Combine(root.FullName, ".lockflight", "features", "20261011-120000-shipping"), feature.RecordDirectory);
    }

    [Fact]
    public void SpecKitsActiveFeatureWinsOverGuessing()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "a"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "b"));
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));
        File.WriteAllText(Path.Combine(root.FullName, ".specify", "feature.json"), """{ "feature_directory": "specs/b" }""");

        Assert.Equal("b", FeatureLocator.Resolve(root.FullName, requested: null).Feature?.Id);
        Assert.Equal("a", FeatureLocator.Resolve(root.FullName, "a").Feature?.Id);
    }

    [Fact]
    public void AmbiguityIsAnErrorThatAsksForTheFeature()
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "a"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "b"));

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null);

        Assert.Null(lookup.Feature);
        Assert.Contains("--feature", lookup.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[\"specs/a\"]")]
    [InlineData("\"specs/a\"")]
    [InlineData("{ \"feature_directory\": 42 }")]
    [InlineData("{ \"feature_directory\": \"\" }")]
    public void AMalformedSpecKitPointerIsAClearErrorNotACrash(string content)
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "a"));
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));
        File.WriteAllText(Path.Combine(root.FullName, ".specify", "feature.json"), content);

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null);

        Assert.Null(lookup.Feature);
        Assert.Contains(".specify/feature.json is not a JSON object", lookup.Error, StringComparison.Ordinal);
        Assert.Equal("a", FeatureLocator.Resolve(root.FullName, "a").Feature?.Id);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
