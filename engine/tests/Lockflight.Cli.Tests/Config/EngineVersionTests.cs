using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Config;

public sealed class EngineVersionTests
{
    [Theory]
    [InlineData("0.2.0-alpha.4", "0.2.0-alpha.4.dev.20261015101143")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-alpha.5")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-alpha.4.dev.20261016000000")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-beta.1")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0")]
    [InlineData("0.2.0", "0.2.1-0.dev.20261015101143")]
    [InlineData("0.2.1-0.dev.20261015101143", "0.2.1-alpha.1")]
    [InlineData("0.2.0-alpha.9", "0.2.0-alpha.10")]
    [InlineData("0.2.0-alpha", "0.2.0-alpha.1")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("0.2.0-dev.20261014223700", "0.2.0-alpha.1")]
    [InlineData("0.2.0-dev.20261014223700", "0.2.0-alpha.6.dev.20261027205103")]
    [InlineData("0.2.0-dev.20261014223700", "0.2.0-dev.20261015000000")]
    [InlineData("0.1.9", "0.2.0-dev.20261014223700")]
    public void VersionsSortBySemanticVersioningPrecedence(string lower, string higher)
    {
        EngineVersion low = EngineVersion.Parse(lower)!;
        EngineVersion high = EngineVersion.Parse(higher)!;

        Assert.True(low.CompareTo(high) < 0);
        Assert.True(high.CompareTo(low) > 0);
    }

    [Fact]
    public void DevelopmentBuildsOfBothSchemesSortBelowTheNextPrerelease()
    {
        EngineVersion release = EngineVersion.Parse("0.2.0-alpha.5")!;

        Assert.True(EngineVersion.Parse("0.2.0-alpha.4.dev.20261015101143")!.CompareTo(release) < 0);
        Assert.True(EngineVersion.Parse("0.2.0-dev.20261015101143")!.CompareTo(release) < 0);
    }

    [Theory]
    [InlineData("0.2.0-ALPHA.4+3f2c1a9", "0.2.0-alpha.4")]
    [InlineData("1.2.3", "1.2.3")]
    public void BuildMetadataAndCaseDoNotChangeTheOrder(string left, string right) =>
        Assert.Equal(0, EngineVersion.Parse(left)!.CompareTo(EngineVersion.Parse(right)!));

    [Theory]
    [InlineData("")]
    [InlineData("0.2")]
    [InlineData("0.2.x")]
    [InlineData("0.2.0-")]
    [InlineData("0.2.0-alpha..1")]
    [InlineData("v0.2.0")]
    public void SomethingThatIsNotAVersionDoesNotParse(string text) => Assert.Null(EngineVersion.Parse(text));

    [Theory]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", true)]
    [InlineData("0.2.1-0.dev.20261015101143", true)]
    [InlineData("0.2.0-dev.20261015101143", true)]
    [InlineData("0.2.0-alpha.4", false)]
    [InlineData("0.2.0-devops.1", false)]
    [InlineData("not a version", false)]
    public void ADevelopmentBuildCarriesTheDevIdentifier(string version, bool expected) =>
        Assert.Equal(expected, EngineVersion.IsDevelopmentBuild(version));

    [Theory]
    [InlineData("0.2.0-alpha.4.dev.20261015101143", "0.2.0-alpha.4")]
    [InlineData("0.2.1-0.dev.20261015101143", "0.2.1-0")]
    [InlineData("0.2.0-dev.20261015101143", "0.2.0-0")]
    [InlineData("0.2.0-alpha.4", "0.2.0-alpha.4")]
    public void TheBaseOfADevelopmentBuildIsTheVersionWithoutItsDevSuffix(string version, string expected) =>
        Assert.Equal(expected, EngineVersion.Parse(version)!.DevelopmentBase().ToString());
}
