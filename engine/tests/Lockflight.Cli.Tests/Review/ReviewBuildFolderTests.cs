using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class ReviewBuildFolderTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-build-folder-");

    [Fact]
    public void AReviewThatStartsWhileAnotherBuildsGetsAFolderOfItsOwnAndRemovesIt()
    {
        File.WriteAllText(Path.Combine(root.FullName, "Shop.csproj"), "<Project />");

        string own;
        using (ReviewBuildFolder first = ReviewBuildFolder.Acquire(root.FullName))
        {
            using ReviewBuildFolder second = ReviewBuildFolder.Acquire(root.FullName);
            Assert.Equal(ReproductionBuild.OutputFolder, first.Name);
            Assert.StartsWith(ReproductionBuild.OutputFolder + "-", second.Name, StringComparison.Ordinal);
            own = second.Name;
            Directory.CreateDirectory(Path.Combine(root.FullName, "obj", own));
            Directory.CreateDirectory(Path.Combine(root.FullName, "obj", first.Name));
        }

        Assert.False(Directory.Exists(Path.Combine(root.FullName, "obj", own)));
        Assert.True(Directory.Exists(Path.Combine(root.FullName, "obj", ReproductionBuild.OutputFolder)));
        using ReviewBuildFolder later = ReviewBuildFolder.Acquire(root.FullName);
        Assert.Equal(ReproductionBuild.OutputFolder, later.Name);
    }

    [Fact]
    public void ALockFileThisUserCannotOpenLeavesTheReviewAFolderOfItsOwn()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Uses Unix file permissions.");
            return;
        }

        string lockFile = ReviewBuildFolder.LockPath(root.FullName);
        File.Delete(lockFile);
        File.WriteAllText(lockFile, string.Empty);
        File.SetUnixFileMode(lockFile, UnixFileMode.None);
        try
        {
            using ReviewBuildFolder folder = ReviewBuildFolder.Acquire(root.FullName);

            Assert.StartsWith(ReproductionBuild.OutputFolder + "-", folder.Name, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(lockFile);
        }
    }

    public void Dispose() => root.Delete(recursive: true);
}
