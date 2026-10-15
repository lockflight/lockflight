using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class PathAliasTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-alias-");

    [Fact]
    public void AWriteThroughASymlinkedDirectoryStillMatchesTheProtectedTarget()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symlinks needs developer mode on Windows.");
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
    [InlineData("C:/repo/secrets/key.txt:hidden", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/secrets./key.txt", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/secrets /key.txt. ", "C:/repo/secrets/key.txt")]
    [InlineData("C:/repo/../repo/a.cs", "C:/repo/../repo/a.cs")]
    public void WindowsTargetsDropStreamsAndTrailingDots(string path, string expected) =>
        Assert.Equal(expected, WindowsPaths.ToWin32Target(path));

    public void Dispose() => projectRoot.Delete(recursive: true);
}
