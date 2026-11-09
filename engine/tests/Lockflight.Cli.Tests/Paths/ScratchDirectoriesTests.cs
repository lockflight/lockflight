using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class ScratchDirectoriesTests
{
    [Fact]
    public async Task ADirectoryAndItsFilesAreDeleted()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-scratch-");
        File.WriteAllText(Path.Combine(directory.FullName, "tool.exe"), "x");

        bool deleted = await ScratchDirectories.TryDeleteAsync(directory, TimeProvider.System, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.False(Directory.Exists(directory.FullName));
    }

    [Fact]
    public async Task ADirectoryThatIsAlreadyGoneCountsAsDeleted()
    {
        DirectoryInfo directory = new(Path.Combine(Path.GetTempPath(), $"lockflight-scratch-gone-{Guid.NewGuid():N}"));

        Assert.True(await ScratchDirectories.TryDeleteAsync(directory, TimeProvider.System, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AFileWindowsStillHoldsLeavesTheDirectoryWithoutAnException()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file another handle holds open.");
        DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-scratch-");
        string file = Path.Combine(directory.FullName, "gitleaks.exe");
        try
        {
            bool deleted;
            using (FileStream held = new(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                deleted = await ScratchDirectories.TryDeleteAsync(directory, TimeProvider.System, TestContext.Current.CancellationToken);
            }

            Assert.False(deleted);
            Assert.True(File.Exists(file));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
