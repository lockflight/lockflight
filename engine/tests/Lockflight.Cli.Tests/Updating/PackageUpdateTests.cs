using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.Updating;

public sealed class PackageUpdateTests : IDisposable
{
    private const string Feed = "/feeds/local";

    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task AFirstInstallPutsTheEngineUnderItsVersionAndLinksIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("0.2.0-alpha.5");

        PackageUpdateResult result = await Update(feed);

        Assert.Equal(new PackageUpdateResult(PackageUpdateStatus.Installed, "0.2.0-alpha.5", null), result);
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        (string fileName, IReadOnlyList<string> arguments, string workingDirectory) = feed.Calls[0];
        Assert.Equal("dotnet", fileName);
        Assert.Equal(UserInstall.Directory(home.FullName), workingDirectory);
        Assert.Contains("--prerelease", arguments);
        Assert.Equal(Feed, arguments[^1]);
        Assert.Equal("--source", arguments[^2]);
        Assert.Equal([UserInstall.ToolPath(home.FullName, "0.2.0-alpha.5")], Directory.GetDirectories(Path.Combine(UserInstall.Directory(home.FullName), "tools")));
        Assert.Equal([UserInstall.Engine(home.FullName), "version"], [feed.Calls[1].FileName, .. feed.Calls[1].Arguments]);
    }

    [Fact]
    public async Task ANewerPackageMovesTheLinkAndKeepsTheOlderEngine()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("0.2.0-alpha.4");
        await Update(feed);
        feed.Newest = "0.2.0-alpha.5";
        time.Advance(TimeSpan.FromMinutes(1));

        PackageUpdateResult result = await Update(feed);

        Assert.Equal(PackageUpdateStatus.Installed, result.Status);
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        Assert.True(Directory.Exists(UserInstall.ToolPath(home.FullName, "0.2.0-alpha.4")));
    }

    [Fact]
    public async Task TheSameVersionIsAlreadyUpToDate()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("0.2.0-alpha.5");
        await Update(feed);

        PackageUpdateResult result = await Update(feed);

        Assert.Equal(new PackageUpdateResult(PackageUpdateStatus.UpToDate, "0.2.0-alpha.5", null), result);
        Assert.Single(Directory.GetDirectories(Path.Combine(UserInstall.Directory(home.FullName), "tools")));
    }

    [Fact]
    public async Task ANewerDevelopmentBuildIsNotReplacedByAnOlderRelease()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("0.2.0-alpha.5.dev.20261015101143");
        await Update(feed);
        feed.Newest = "0.2.0-alpha.5";

        PackageUpdateResult result = await Update(feed);

        Assert.Equal(new PackageUpdateResult(PackageUpdateStatus.KeptNewer, "0.2.0-alpha.5.dev.20261015101143", null), result);
        Assert.Equal("0.2.0-alpha.5.dev.20261015101143", UserInstall.InstalledVersion(home.FullName));
        Assert.Single(Directory.GetDirectories(Path.Combine(UserInstall.Directory(home.FullName), "tools")));
    }

    [Fact]
    public async Task AReleaseInstallFollowsReleasesOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("1.0.0");
        await Update(feed);
        feed.Newest = "1.0.1";

        await Update(feed);

        Assert.DoesNotContain("--prerelease", feed.Calls[^2].Arguments);
        Assert.Equal("1.0.1", UserInstall.InstalledVersion(home.FullName));
    }

    [Fact]
    public async Task AFailedInstallSaysWhyAndLeavesTheInstallAsItWas()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        FakeToolFeed feed = new("0.2.0-alpha.4");
        await Update(feed);
        feed.Fails = true;

        PackageUpdateResult result = await Update(feed);

        Assert.Equal(PackageUpdateStatus.Failed, result.Status);
        Assert.Contains("NU1101", result.Problem, StringComparison.Ordinal);
        Assert.Equal("0.2.0-alpha.4", UserInstall.InstalledVersion(home.FullName));
        Assert.Single(Directory.GetDirectories(Path.Combine(UserInstall.Directory(home.FullName), "tools")));
    }

    [Fact]
    public async Task APackageInstalledStraightIntoBinMovesIntoTheVersionedLayout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string bin = Path.GetDirectoryName(UserInstall.Engine(home.FullName))!;
        FakeToolFeed.LayOut(bin, "0.2.0-alpha.4");
        Assert.Equal("0.2.0-alpha.4", UserInstall.InstalledVersion(home.FullName));

        PackageUpdateResult result = await Update(new FakeToolFeed("0.2.0-alpha.5"));

        Assert.Equal(PackageUpdateStatus.Installed, result.Status);
        Assert.Equal("0.2.0-alpha.5", UserInstall.InstalledVersion(home.FullName));
        Assert.False(Directory.Exists(Path.Combine(bin, ".store")));
    }

    private Task<PackageUpdateResult> Update(FakeToolFeed feed) =>
        new PackageUpdate(feed.RunAsync, time).RunAsync(home.FullName, Feed, TestContext.Current.CancellationToken);

    public void Dispose() => TempDirectories.DeleteRecursively(home);
}
