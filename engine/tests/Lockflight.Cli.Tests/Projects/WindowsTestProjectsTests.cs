using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Tests.Projects;

public sealed class WindowsTestProjectsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-desktop-");

    [Fact]
    public void ATestProjectOnAWindowsFrameworkNeedsWindowsAndAServerOneDoesNot()
    {
        string client = DesktopProjects.WriteWpfTests(root.FullName);
        DesktopProjects.WriteServerTests(root.FullName);

        Assert.Equal([client], ProjectFiles.WindowsTestProjects(root.FullName));
    }

    [Theory]
    [InlineData("<PropertyGroup><TargetFrameworks>net8.0;net8.0-windows10.0.19041.0</TargetFrameworks></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWPF> True </UseWPF></PropertyGroup>")]
    public void AWindowsFrameworkAmongSeveralOrWpfOrWindowsFormsNeedsWindows(string properties)
    {
        string project = DesktopProjects.Write(root.FullName, "tests/A.Tests/A.Tests.csproj", properties);

        Assert.Equal([project], ProjectFiles.WindowsTestProjects(root.FullName));
    }

    [Fact]
    public void WpfTurnedOnInADirectoryBuildFileAboveTheTestProjectCounts()
    {
        File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), "<Project><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>");
        string project = DesktopProjects.WriteServerTests(root.FullName);

        Assert.Equal([project], ProjectFiles.WindowsTestProjects(root.FullName));
    }

    [Fact]
    public void ADesktopAppThatIsNotATestProjectAndWpfTurnedOffDoNotCount()
    {
        DesktopProjects.Write(root.FullName, "src/Client/Client.csproj", "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>", package: "CommunityToolkit.Mvvm");
        DesktopProjects.Write(root.FullName, "tests/A.Tests/A.Tests.csproj", "<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWPF>false</UseWPF></PropertyGroup>");

        Assert.Empty(ProjectFiles.WindowsTestProjects(root.FullName));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
