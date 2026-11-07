using Lockflight.Cli.Gates.WindowsTargets;

namespace Lockflight.Cli.Tests.Gates.WindowsTargets;

public sealed class WindowsTargetingTests
{
    [Fact]
    public void TheProjectsThatTargetWindowsAndTheTestProjectsAreFound()
    {
        using DesktopSolution solution = new();

        WindowsTargetedSolution? found = WindowsTargeting.Find(solution.Root, buildFile: null);

        Assert.NotNull(found);
        Assert.Equal([@"src\Desk.Client\Desk.Client.csproj", @"tests\Desk.Client.Tests\Desk.Client.Tests.csproj"], found.WindowsEntries);
        Assert.Equal(["Desk.Client.Tests"], found.WindowsTestsAmong(found.Entries));
        Assert.Contains(@"tests\Desk.Server.Tests\Desk.Server.Tests.csproj", found.TestEntries);
    }

    [Fact]
    public void AFrameworkSetInDirectoryBuildPropsCountsUnlessTheProjectSetsItsOwn()
    {
        using DesktopSolution solution = new();
        solution.Write("src/Desk.Client/Desk.Client.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        solution.Write("src/Directory.Build.props", "<Project><PropertyGroup><TargetFrameworks>net8.0;net8.0-windows10.0.19041.0</TargetFrameworks></PropertyGroup></Project>\n");

        WindowsTargetedSolution? found = WindowsTargeting.Find(solution.Root, buildFile: null);

        Assert.Contains(@"src\Desk.Client\Desk.Client.csproj", found!.WindowsEntries);
        Assert.DoesNotContain(@"src\Desk.Server\Desk.Server.csproj", found.WindowsEntries);
    }

    [Fact]
    public void ACommentedOutFrameworkDoesNotCount()
    {
        using DesktopSolution solution = new();
        solution.Write("src/Desk.Client/Desk.Client.csproj", DesktopSolution.ServerProject.Replace("<PropertyGroup>", "<PropertyGroup><!-- <TargetFramework>net8.0-windows</TargetFramework> -->"));
        solution.Write("tests/Desk.Client.Tests/Desk.Client.Tests.csproj", DesktopSolution.ServerTestProject);

        Assert.Null(WindowsTargeting.Find(solution.Root, buildFile: null));
    }

    [Fact]
    public void AConditionalWindowsFrameworkOrWpfOnlyCounts()
    {
        using DesktopSolution solution = new();
        solution.Write("src/Desk.Client/Desk.Client.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework Condition=\"'$(OS)' == 'Windows_NT'\">net8.0-windows</TargetFramework></PropertyGroup></Project>\n");
        solution.Write("tests/Desk.Client.Tests/Desk.Client.Tests.csproj", DesktopSolution.ServerTestProject.Replace("</TargetFramework>", "</TargetFramework><UseWPF>true</UseWPF>"));

        WindowsTargetedSolution? found = WindowsTargeting.Find(solution.Root, buildFile: null);

        Assert.Equal([@"src\Desk.Client\Desk.Client.csproj", @"tests\Desk.Client.Tests\Desk.Client.Tests.csproj"], found!.WindowsEntries);
    }

    [Fact]
    public void TheBuildFileNamesTheSolution()
    {
        using DesktopSolution solution = new();
        File.Move(solution.SolutionFile, Path.Combine(solution.Root, "All.sln"));
        solution.Write("src/Other.sln", "Microsoft Visual Studio Solution File\n");

        Assert.NotNull(WindowsTargeting.Find(solution.Root, "All.sln"));
        Assert.Null(WindowsTargeting.Find(solution.Root, "src/Desk.Client/Desk.Client.csproj"));
    }

    [Fact]
    public void NoSingleSolutionFindsNothing()
    {
        using DesktopSolution solution = new();
        solution.Write("Other.sln", "Microsoft Visual Studio Solution File\n");

        Assert.Null(WindowsTargeting.Find(solution.Root, buildFile: null));
    }

    [Theory]
    [InlineData("net8.0-windows", true)]
    [InlineData("net8.0-windows10.0.19041.0", true)]
    [InlineData("NET9.0-WINDOWS", true)]
    [InlineData("net8.0", false)]
    [InlineData("net8.0-android", false)]
    public void AWindowsFrameworkNamesTheWindowsPlatform(string framework, bool windows) =>
        Assert.Equal(windows, WindowsTargeting.IsWindowsFramework(framework));

    [Fact]
    public void TheNoteListsTheProjectsLeftOut() =>
        Assert.Equal(
            "left out off Windows: A, B, C, D and 1 more (they target Windows and cannot start here; they run on Windows and on a Windows CI runner)",
            WindowsTargetedSolution.LeftOutNote(["A", "B", "C", "D", "E"]));
}
