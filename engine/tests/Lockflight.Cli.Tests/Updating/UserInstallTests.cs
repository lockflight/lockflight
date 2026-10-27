using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.Updating;

public sealed class UserInstallTests : IDisposable
{
    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    [Fact]
    public void ACopiedEngineIsTheStoresNativeEngineAndRecordsItsVersion()
    {
        FakeToolFeed.LayOut(UserInstall.ToolPath(home.FullName, "0.2.0-alpha.5"), "0.2.0-alpha.5", EngineLayout.Copy);

        UserInstall.PointAt(home.FullName, "0.2.0-alpha.5", EngineLayout.Copy);

        Assert.Equal(FakeToolFeed.EngineContent("0.2.0-alpha.5"), File.ReadAllText(UserInstall.Engine(home.FullName)));
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(UserInstall.Engine(home.FullName)).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public void PointingACopiedEngineAtAnotherVersionReplacesTheCopyAndItsVersion()
    {
        FakeToolFeed.LayOut(UserInstall.ToolPath(home.FullName, "0.2.0-alpha.4"), "0.2.0-alpha.4", EngineLayout.Copy);
        FakeToolFeed.LayOut(UserInstall.ToolPath(home.FullName, "0.2.0-alpha.5"), "0.2.0-alpha.5", EngineLayout.Copy);
        UserInstall.PointAt(home.FullName, "0.2.0-alpha.4", EngineLayout.Copy);

        UserInstall.PointAt(home.FullName, "0.2.0-alpha.5", EngineLayout.Copy);

        Assert.Equal(FakeToolFeed.EngineContent("0.2.0-alpha.5"), File.ReadAllText(UserInstall.Engine(home.FullName)));
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        Assert.Equal(
            [UserInstall.BinaryName, UserInstall.VersionFileName],
            Directory.GetFiles(Path.GetDirectoryName(UserInstall.Engine(home.FullName))!).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ARecordedVersionWithoutTheEngineIsNoInstall()
    {
        string bin = Path.GetDirectoryName(UserInstall.Engine(home.FullName))!;
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, UserInstall.VersionFileName), "0.2.0-alpha.5\n");

        Assert.Null(UserInstall.InstalledVersion(home.FullName));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(home);
}
