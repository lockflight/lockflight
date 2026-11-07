using Lockflight.Cli.Gates.WindowsTargets;

namespace Lockflight.Cli.Tests.Projects;

public sealed class WindowsTestProjectsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-desktop-");

    [Fact]
    public void ATestProjectOnAWindowsFrameworkNeedsWindowsAndAServerOneDoesNot()
    {
        string client = DesktopProjects.WriteWpfTests(root.FullName);
        DesktopProjects.WriteServerTests(root.FullName);

        Assert.Equal([client], WindowsTestProjects());
    }

    [Theory]
    [InlineData("<PropertyGroup><TargetFrameworks>net8.0;net8.0-windows10.0.19041.0</TargetFrameworks></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWindowsForms>true</UseWindowsForms></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWPF> True </UseWPF></PropertyGroup>")]
    [InlineData("<PropertyGroup><TargetFramework>net8.0</TargetFramework><TargetFramework Condition=\"'$(OS)' == 'Windows_NT'\">net8.0-windows</TargetFramework></PropertyGroup>")]
    public void AWindowsFrameworkAmongSeveralOrAConditionalOneOrWpfOrWindowsFormsNeedsWindows(string properties)
    {
        string project = DesktopProjects.Write(root.FullName, "tests/A.Tests/A.Tests.csproj", properties);

        Assert.Equal([project], WindowsTestProjects());
    }

    [Fact]
    public void WpfTurnedOnInADirectoryBuildPropsAboveTheTestProjectCounts()
    {
        File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.props"), "<Project><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>");
        string project = DesktopProjects.WriteServerTests(root.FullName);

        Assert.Equal([project], WindowsTestProjects());
    }

    [Fact]
    public void ACommentedOutWpfOrAWindowsFrameworkInDirectoryBuildTargetsDoesNotCount()
    {
        DesktopProjects.Write(root.FullName, "tests/A.Tests/A.Tests.csproj", "<PropertyGroup><TargetFramework>net8.0</TargetFramework><!-- <UseWPF>true</UseWPF> --></PropertyGroup>");
        File.WriteAllText(Path.Combine(root.FullName, "Directory.Build.targets"), "<Project><PropertyGroup><UseWindowsForms>true</UseWindowsForms></PropertyGroup></Project>");

        Assert.Empty(WindowsTestProjects());
    }

    [Fact]
    public void ADesktopAppThatIsNotATestProjectAndWpfTurnedOffDoNotCount()
    {
        DesktopProjects.Write(root.FullName, "src/Client/Client.csproj", "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>", package: "CommunityToolkit.Mvvm");
        DesktopProjects.Write(root.FullName, "tests/A.Tests/A.Tests.csproj", "<PropertyGroup><TargetFramework>net8.0</TargetFramework><UseWPF>false</UseWPF></PropertyGroup>");

        Assert.Empty(WindowsTestProjects());
        Assert.True(WindowsTargeting.BuildsWindowsProjects(new BuildScope(root.FullName, BuildFile: null)));
    }

    [Fact]
    public void AWpfTestProjectTheSolutionDoesNotListDoesNotCount()
    {
        DesktopProjects.WriteServerTests(root.FullName);
        DesktopProjects.WriteOutsideSolution(root.FullName, "samples/Wpf.Tests/Wpf.Tests.csproj", "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>");

        Assert.Empty(WindowsTestProjects());
        Assert.False(WindowsTargeting.BuildsWindowsProjects(new BuildScope(root.FullName, BuildFile: null)));
    }

    [Fact]
    public void TheBuildFileChoosesWhatIsBuilt()
    {
        string wpf = DesktopProjects.WriteWpfTests(root.FullName);
        File.Move(Path.Combine(root.FullName, DesktopProjects.SolutionName), Path.Combine(root.FullName, "Desktop.sln"));
        string server = DesktopProjects.WriteServerTests(root.FullName);

        Assert.Equal([wpf], WindowsTargeting.WindowsTestProjects([new BuildScope(root.FullName, "Desktop.sln")]));
        Assert.Empty(WindowsTargeting.WindowsTestProjects([new BuildScope(root.FullName, DesktopProjects.SolutionName)]));
        Assert.Empty(WindowsTargeting.WindowsTestProjects([new BuildScope(root.FullName, Path.GetRelativePath(root.FullName, server))]));
        Assert.Empty(WindowsTargeting.WindowsTestProjects([new BuildScope(root.FullName, BuildFile: null)]));
    }

    [Fact]
    public void ASolutionFilterBuildsOnlyTheProjectsItKeeps()
    {
        DesktopProjects.WriteWpfTests(root.FullName);
        DesktopProjects.WriteServerTests(root.FullName);
        File.WriteAllText(Path.Combine(root.FullName, "Server.slnf"), $$"""{ "solution": { "path": "{{DesktopProjects.SolutionName}}", "projects": [ "tests\\Server.Tests\\Server.Tests.csproj" ] } }""");

        Assert.Empty(WindowsTargeting.WindowsTestProjects([new BuildScope(root.FullName, "Server.slnf")]));
    }

    private IReadOnlyList<string> WindowsTestProjects() => WindowsTargeting.WindowsTestProjects(BuildScope.OfRepository(root.FullName));

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
