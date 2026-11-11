using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class WindowsPathsTests
{
    [Theory]
    [InlineData(@"C:\Users\RUNNER~1\AppData", true)]
    [InlineData(@"C:\PROGRA~1\app.exe", true)]
    [InlineData(@"C:\Users\runneradmin\AppData", false)]
    [InlineData("/home/user/src/a.cs", false)]
    public void EightDotThreeAliasesAreRecognised(string path, bool expected) =>
        Assert.Equal(expected, WindowsPaths.HasShortNameSegment(path));

    [Theory]
    [InlineData(@"\\?\C:\repo\lockflight.yaml", @"C:\repo\lockflight.yaml")]
    [InlineData(@"\\.\C:\repo\lockflight.yaml", @"C:\repo\lockflight.yaml")]
    [InlineData("//?/C:/repo/lockflight.yaml", @"C:\repo\lockflight.yaml")]
    [InlineData(@"\\?\UNC\localhost\C$\repo\x", @"C:\repo\x")]
    [InlineData(@"\\localhost\c$\repo\x", @"c:\repo\x")]
    [InlineData(@"\\127.0.0.1\D$\repo\x", @"D:\repo\x")]
    [InlineData(@"\\?\UNC\fileserver\team\x", @"\\fileserver\team\x")]
    [InlineData(@"\\fileserver\team\x", @"\\fileserver\team\x")]
    [InlineData(@"C:\repo\x", @"C:\repo\x")]
    [InlineData("src/a.cs", "src/a.cs")]
    public void NamespaceAndAdminSharePathsMapToTheirDrive(string path, string expected) =>
        Assert.Equal(expected, WindowsPaths.ToDosPath(path));

    [Theory]
    [InlineData(@"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\repo\x")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume3\repo\x")]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\localhost\projects\repo\x")]
    [InlineData(@"\\buildhost\C$\repo\x")]
    public void DevicePathsTheGuardCannotMapAreUnresolvable(string path) =>
        Assert.Null(WindowsPaths.ToDosPath(path));

    [Fact]
    public void OffWindowsAPathWithATildeIsLeftAsWritten()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows expands short names.");

        Assert.Equal("/home/user/RUNNER~1/x", WindowsPaths.ExpandShortNames("/home/user/RUNNER~1/x"));
    }

    [Fact]
    public async Task AShortNameOfAnExistingFolderExpandsToItsLongName()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "8.3 short names are a Windows file-system alias.");
        DirectoryInfo folder = Directory.CreateTempSubdirectory("lockflight-long-folder-name-");
        try
        {
            string? alias = await WindowsShortNames.OfFolderAsync(folder.FullName, TestContext.Current.CancellationToken);
            Assert.SkipWhen(alias is null, "this volume keeps no 8.3 short names.");

            string expanded = WindowsPaths.ExpandShortNames(Path.Combine(alias!, "missing", "file.txt"));

            Assert.Equal(Path.Combine(folder.FullName, "missing", "file.txt"), expanded, ignoreCase: true);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
