using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class GlobOverlapTests
{
    [Theory]
    [InlineData("secrets/**", "k", false, true)]
    [InlineData("secrets/**", "secrets/k", false, true)]
    [InlineData("lockflight.yaml", "lockflight.yaml", false, true)]
    [InlineData("lockflight.yaml", "notes.txt", false, false)]
    [InlineData("**/Directory.Build.props", "src/Directory.Build.props", false, true)]
    [InlineData("**/Directory.Build.props", "src/Program.cs", false, false)]
    [InlineData("**/*.ruleset", "*.cs", false, false)]
    [InlineData("**/*.ruleset", "*", false, true)]
    [InlineData("global.json", "*.json", false, true)]
    [InlineData("global.json", "[gG]lobal.json", false, true)]
    [InlineData("global.json", "src", true, false)]
    [InlineData("tests/Shop.Tests/**", "Shop.Tests", true, true)]
    [InlineData("lockflight.yaml", "", true, true)]
    public void AGlobCouldMatchAPathBelowAnUnknownDirectory(string glob, string floatingPath, bool asTree, bool expected) =>
        Assert.Equal(expected, GlobOverlap.CouldMatchBelowAnyDirectory(glob, floatingPath, asTree));

    [Fact]
    public void WildcardsExpandAgainstExistingEntriesOnly()
    {
        DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-wild-");
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "a.json"), "{}");
            File.WriteAllText(Path.Combine(root.FullName, ".hidden.json"), "{}");
            File.WriteAllText(Path.Combine(root.FullName, "b.cs"), "");

            IReadOnlyList<string>? matches = PathWildcards.Expand("*.json", root.FullName, maxMatches: 10);

            Assert.NotNull(matches);
            Assert.Equal(["a.json"], matches.Select(Path.GetFileName));
            Assert.Null(PathWildcards.Expand("*", root.FullName, maxMatches: 1));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
