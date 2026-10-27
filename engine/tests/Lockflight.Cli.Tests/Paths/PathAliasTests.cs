using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class PathAliasTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-alias-");

    [Fact]
    public void AWriteThroughASymlinkedDirectoryStillMatchesTheProtectedTarget()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "secrets"));
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "innocent"), Path.Combine(projectRoot.FullName, "secrets"));

        string? match = PathRules.FindMatch(projectRoot.FullName, Path.Combine(projectRoot.FullName, "innocent", "key.txt"), ["secrets/**"]);

        Assert.Equal("secrets/**", match);
    }

    [Fact]
    public void MacOsPrivateAliasesResolveToTheSameProject()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS-only firmlink.");
        string viaVar = Path.Combine("/var/tmp", projectRoot.Name);
        Directory.CreateDirectory(viaVar);
        try
        {
            string viaPrivate = Path.Combine("/private/var/tmp", projectRoot.Name, "secrets", "key.txt");

            Assert.Equal("secrets/**", PathRules.FindMatch(viaVar, viaPrivate, ["secrets/**"]));
        }
        finally
        {
            Directory.Delete(viaVar, recursive: true);
        }
    }

    [Theory]
    [InlineData("l/../../lockflight.yaml", "lockflight.yaml")]
    [InlineData("l/../evidence.json", ".lockflight/**")]
    [InlineData("l/./../../secrets/key.txt", "secrets/**")]
    public void AParentSegmentAfterASymlinkGoesToTheParentOfItsTarget(string target, string expectedRule)
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight", "local"));
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "l"), Path.Combine(projectRoot.FullName, ".lockflight", "local"));

        string? match = PathRules.FindMatch(projectRoot.FullName, target, projectRoot.FullName, ["lockflight.yaml", "secrets/**", ".lockflight/**"]);

        Assert.Equal(expectedRule, match);
    }

    [Fact]
    public void AParentSegmentAfterASymlinkToADirectoryElsewhereLeavesTheProject()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        DirectoryInfo elsewhere = Directory.CreateTempSubdirectory("lockflight-elsewhere-");
        try
        {
            Directory.CreateDirectory(Path.Combine(elsewhere.FullName, "inner"));
            Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "out"), Path.Combine(elsewhere.FullName, "inner"));

            Assert.Null(PathRules.FindMatch(projectRoot.FullName, "out/../lockflight.yaml", projectRoot.FullName, ["lockflight.yaml"]));
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("secrets/cafe\u0301.txt", "secrets/caf\u00e9.txt")]
    [InlineData("secrets/caf\u00e9.txt", "secrets/cafe\u0301.txt")]
    [InlineData("docs/\u0457.md", "docs/\u0456\u0308.md")]
    public void DecomposedAndComposedSpellingsOfANameMatchTheSameRule(string path, string glob) =>
        Assert.Equal(glob, PathRules.FindMatch(projectRoot.FullName, path, projectRoot.FullName, [glob]));

    [Fact]
    public void ADecomposedSpellingIsJudgedByTheRuleOverlapToo() =>
        Assert.True(GlobOverlap.CouldMatchBelowAnyDirectory("docs/caf\u00e9.md", "cafe\u0301.md", asTree: false));

    [Theory]
    [InlineData("C:/repo/secrets/key.txt:hidden", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/secrets./key.txt", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/secrets /key.txt. ", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/../repo/a.cs", "C:/repo/../repo/a.cs")]
    public void WindowsTargetsDropStreamsAndTrailingDots(string path, string expected) =>
        Assert.Equal(expected, WindowsPaths.ToWin32Target(path));

    public void Dispose() => projectRoot.Delete(recursive: true);
}
