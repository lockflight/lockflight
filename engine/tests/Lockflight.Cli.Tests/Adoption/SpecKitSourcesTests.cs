using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class SpecKitSourcesTests
{
    [Fact]
    public void AReleaseComesFromPyPiAndElseFromItsGitHubTag()
    {
        IReadOnlyList<SpecKitSource> sources = SpecKitSources.For("1.1.0");

        Assert.Equal(["specify-cli==1.1.0", "git+https://github.com/github/spec-kit@v1.1.0"], sources.Select(source => source.From));
        Assert.True(sources[0].IsPackageIndex);
        Assert.False(sources[1].IsPackageIndex);
        Assert.Equal("GitHub tag v1.1.0", sources[1].Name);
    }

    [Fact]
    public void APreReleaseHasNoTagToFallBackOn() =>
        Assert.Equal(["specify-cli==1.0.5.dev0"], SpecKitSources.For("1.0.5.dev0").Select(source => source.From));

    [Theory]
    [InlineData("main")]
    [InlineData("1.1.0 @ file:///tmp/other")]
    [InlineData("1.1.0;python_version>'3'")]
    [InlineData("")]
    public void AnythingButAVersionHasNoSource(string version) => Assert.Empty(SpecKitSources.For(version));
}
