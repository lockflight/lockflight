using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class PathRulesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "lockflight-root");

    [Theory]
    [InlineData("secrets/key.txt", "secrets/**", true)]
    [InlineData("secrets/nested/deep/key.txt", "secrets/**", true)]
    [InlineData("src/secrets.cs", "secrets/**", false)]
    [InlineData("tests/Unit/FooTests.cs", "**/*Tests.cs", true)]
    [InlineData("FooTests.cs", "**/*Tests.cs", true)]
    [InlineData("SECRETS/KEY.TXT", "secrets/**", true)]
    [InlineData("packages.lock", "*.lock", true)]
    [InlineData("sub/packages.lock", "*.lock", false)]
    public void GlobsMatchRelativeToTheProjectRoot(string relativePath, string glob, bool expected)
    {
        string? match = PathRules.FindMatch(Root, Path.Combine(Root, relativePath), [glob]);

        Assert.Equal(expected, match is not null);
    }

    [Fact]
    public void WindowsSeparatorsMatchTheSameRules()
    {
        string windowsStylePath = Path.Combine(Root, "secrets").Replace('/', '\\') + "\\key.txt";

        Assert.NotNull(PathRules.FindMatch(Root.Replace('/', '\\'), windowsStylePath, ["secrets/**"]));
    }

    [Fact]
    public void ARelativePathResolvesAgainstAWorkingDirectoryWithWindowsSeparators()
    {
        string windowsStyleRoot = Root.Replace('/', '\\');

        Assert.NotNull(PathRules.FindMatch(windowsStyleRoot, "secrets/key.txt", windowsStyleRoot, ["secrets/**"]));
    }

    [Fact]
    public void PathsOutsideTheRootNeverMatch()
    {
        string outside = Path.Combine(Path.GetTempPath(), "elsewhere", "secrets", "key.txt");

        Assert.Null(PathRules.FindMatch(Root, outside, ["secrets/**"]));
    }
}
