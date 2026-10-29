using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class GlobOverlapTests
{
    [Theory]
    [InlineData("secrets/**", "secrets/k", false, true)]
    [InlineData("secrets/**", "secrets/*", false, true)]
    [InlineData("lockflight.yaml", "lockflight.yaml", false, true)]
    [InlineData("lockflight.yaml", "notes.txt", false, false)]
    [InlineData("**/Directory.Build.props", "src/Directory.Build.props", false, true)]
    [InlineData("**/Directory.Build.props", "src/Program.cs", false, false)]
    [InlineData("**/*.ruleset", "*.cs", false, false)]
    [InlineData("global.json", "*.json", false, true)]
    [InlineData("global.json", "[gG]lobal.json", false, true)]
    [InlineData("global.json", "src", true, false)]
    [InlineData("tests/Shop.Tests/A.cs", "tests/*", true, true)]
    [InlineData("tests/Shop.Tests/A.cs", "tests/*", false, false)]
    public void AGlobCouldMatchAPathWithWildcards(string glob, string path, bool asTree, bool expected) =>
        Assert.Equal(expected, GlobOverlap.CouldMatch(glob, path, asTree));
}
