using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Processes;

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

    [Theory]
    [InlineData("006-weak-spots", "006-weak-spots")]
    [InlineData("006-weak-spots-v2", "006-weak-spots")]
    [InlineData("feat/006-weak-spots", "006-weak-spots")]
    [InlineData("jdoe/web/006-anything", "006-weak-spots")]
    [InlineData("20261011-120000-shipping", "20261011-120000-shipping")]
    public void AFeatureBranchNamesTheFeatureWithItsNumber(string branch, string expected)
    {
        CreateFeatures("005-cart", "006-weak-spots", "20261011-120000-shipping");
        CheckOut(branch);

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, NoVariables);

        Assert.Null(lookup.Error);
        Assert.Equal(expected, lookup.Feature?.Id);
    }

    [Fact]
    public async Task ABranchCheckedOutWithGitNamesTheFeature()
    {
        CreateFeatures("005-cart", "006-weak-spots");
        await ProcessRunner.RunAsync("git", ["init", "-q", "-b", "006-weak-spots"], root.FullName, TestContext.Current.CancellationToken);

        Assert.Equal("006-weak-spots", FeatureLocator.Resolve(root.FullName, requested: null, NoVariables).Feature?.Id);
    }

    [Fact]
    public void AFeatureBranchInALinkedWorktreeNamesTheFeature()
    {
        CreateFeatures("005-cart", "006-weak-spots");
        string gitDirectory = Path.Combine(root.FullName, "elsewhere", "worktrees", "wt");
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/006-weak-spots\n");
        File.WriteAllText(Path.Combine(root.FullName, ".git"), $"gitdir: {gitDirectory}\n");

        Assert.Equal("006-weak-spots", FeatureLocator.Resolve(root.FullName, requested: null, NoVariables).Feature?.Id);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("fix/typo")]
    [InlineData("12-short")]
    [InlineData("007-not-written-yet")]
    public void ABranchThatNamesNoFeatureLeavesTheChoiceToTheOtherRules(string branch)
    {
        CreateFeatures("005-cart", "006-weak-spots");
        CheckOut(branch);

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, NoVariables);

        Assert.Null(lookup.Feature);
        Assert.Contains("no active feature", lookup.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ABranchWhoseNumberSeveralFeaturesShareIsAnError()
    {
        CreateFeatures("002-orders", "002-billing");
        CheckOut("002-refunds");

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, NoVariables);

        Assert.Null(lookup.Feature);
        Assert.Contains("branch '002-refunds' matches several features under specs/: 002-billing, 002-orders", lookup.Error, StringComparison.Ordinal);
        Assert.Contains("--feature", lookup.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ADetachedHeadNamesNoFeature()
    {
        CreateFeatures("006-weak-spots");
        Directory.CreateDirectory(Path.Combine(root.FullName, ".git"));
        File.WriteAllText(Path.Combine(root.FullName, ".git", "HEAD"), "0123456789abcdef0123456789abcdef01234567\n");

        Assert.Equal("006-weak-spots", FeatureLocator.Resolve(root.FullName, requested: null, NoVariables).Feature?.Id);
    }

    [Fact]
    public void SpecKitsActiveFeatureFileWinsOverTheBranch()
    {
        CreateFeatures("005-cart", "006-weak-spots");
        CheckOut("006-weak-spots");
        File.WriteAllText(Path.Combine(root.FullName, ".specify", "feature.json"), """{ "feature_directory": "specs/005-cart" }""");

        Assert.Equal("005-cart", FeatureLocator.Resolve(root.FullName, requested: null, NoVariables).Feature?.Id);
    }

    [Fact]
    public void SpecKitsFeatureVariableWinsOverTheBranch()
    {
        CreateFeatures("005-cart", "006-weak-spots");
        CheckOut("006-weak-spots");

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, Variables(EnvironmentVariables.SpecifyFeature, "005-cart"));

        Assert.Equal("005-cart", lookup.Feature?.Id);
    }

    [Fact]
    public void SpecKitsFeatureDirectoryVariableWinsOverTheActiveFeatureFile()
    {
        CreateFeatures("005-cart", "006-weak-spots");
        File.WriteAllText(Path.Combine(root.FullName, ".specify", "feature.json"), """{ "feature_directory": "specs/005-cart" }""");

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, Variables(EnvironmentVariables.SpecifyFeatureDirectory, "specs/006-weak-spots"));

        Assert.Equal("006-weak-spots", lookup.Feature?.Id);
        Assert.Equal("005-cart", FeatureLocator.Resolve(root.FullName, "005-cart", Variables(EnvironmentVariables.SpecifyFeatureDirectory, "specs/006-weak-spots")).Feature?.Id);
    }

    [Theory]
    [InlineData(EnvironmentVariables.SpecifyFeature, "009-missing", "SPECIFY_FEATURE '009-missing' names no feature under specs/")]
    [InlineData(EnvironmentVariables.SpecifyFeatureDirectory, "specs/009-missing", "SPECIFY_FEATURE_DIRECTORY 'specs/009-missing' is not a directory")]
    public void ASpecKitVariableThatNamesNoFeatureIsAnErrorNotAGuess(string variable, string value, string expected)
    {
        CreateFeatures("006-weak-spots");

        FeatureLookup lookup = FeatureLocator.Resolve(root.FullName, requested: null, Variables(variable, value));

        Assert.Null(lookup.Feature);
        Assert.Contains(expected, lookup.Error, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static EnvironmentReader NoVariables => EnvironmentReader.Of(new Dictionary<string, string>());

    private static EnvironmentReader Variables(string name, string value) => EnvironmentReader.Of(new Dictionary<string, string> { [name] = value });

    private void CreateFeatures(params string[] ids)
    {
        foreach (string id in ids)
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "specs", id));
        }

        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));
    }

    private void CheckOut(string branch)
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, ".git"));
        File.WriteAllText(Path.Combine(root.FullName, ".git", "HEAD"), $"ref: refs/heads/{branch}\n");
    }
}
