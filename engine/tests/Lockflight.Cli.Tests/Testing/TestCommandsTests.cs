using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestCommandsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-commands-");

    [Fact]
    public void OnVSTestDotnetTestSelectsTheMethodsByFullyQualifiedNameInTheProjectDirectory()
    {
        TestApp app = App(RunnerMode.Unknown);

        TestCommand command = TestCommands.DotnetTest(app, ["Shop.Tests.PriceTests.Costs", "Shop.Tests.PriceTests.Rounds"], noBuild: false)!;

        Assert.Equal(app.ProjectDirectory, command.WorkingDirectory);
        Assert.Equal(["test", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=Shop.Tests.PriceTests.Costs|FullyQualifiedName=Shop.Tests.PriceTests.Rounds"], command.Arguments);
    }

    [Fact]
    public void OnTheTestingPlatformAnXunitAppIsFilteredByMethodAndTheProjectIsAnOption()
    {
        File.WriteAllText(Path.Combine(root.FullName, "global.json"), """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");

        TestCommand command = TestCommands.DotnetTest(App(RunnerMode.Native) with { Framework = "net10.0" }, ["Shop.Tests.PriceTests.Costs"], noBuild: true)!;

        Assert.Equal(["test", "--project", "Shop.Tests.csproj", "--no-build", "--framework", "net10.0", "--filter-method", "Shop.Tests.PriceTests.Costs"], command.Arguments);
    }

    [Fact]
    public void OnTheTestingPlatformAnMSTestAppKeepsTheVSTestFilter()
    {
        File.WriteAllText(Path.Combine(root.FullName, "global.json"), """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");

        TestCommand command = TestCommands.DotnetTest(App(RunnerMode.MSTestMtp), ["Shop.Tests.PriceTests.Costs"], noBuild: false)!;

        Assert.Equal(["test", "--project", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=Shop.Tests.PriceTests.Costs"], command.Arguments);
    }

    [Fact]
    public void AnNUnitTestIsMatchedWithItsParameterizedCases()
    {
        TestCommand command = TestCommands.DotnetTest(App(RunnerMode.NUnit), ["Shop.Tests.Prices.Costs"], noBuild: false)!;

        Assert.Equal(["test", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=Shop.Tests.Prices.Costs|FullyQualifiedName~Shop.Tests.Prices.Costs\\("], command.Arguments);
    }

    [Fact]
    public void AnMSTestOrXunitV2AppOnVSTestIsMatchedByFullyQualifiedName()
    {
        Assert.Equal(["test", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=A.B.C"], TestCommands.DotnetTest(App(RunnerMode.MSTest), ["A.B.C"], noBuild: false)!.Arguments);
        Assert.Equal(["test", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=A.B.C"], TestCommands.DotnetTest(App(RunnerMode.VSTest), ["A.B.C"], noBuild: false)!.Arguments);
    }

    [Fact]
    public void AnAppWithoutItsProjectFileHasNoDotnetTestCommand() =>
        Assert.Null(TestCommands.DotnetTest(App(RunnerMode.Native) with { ProjectFile = null }, ["A.B.C"], noBuild: false));

    [Fact]
    public void TheEngineRunsAnXunitAppInItsOutputFolder()
    {
        TestApp app = App(RunnerMode.Native);

        TestCommand command = TestCommands.EngineRun(app, ["Shop.Tests.PriceTests.Costs"]);

        Assert.Equal(app.OutputDirectory, command.WorkingDirectory);
        Assert.Equal([app.AssemblyPath, "-noLogo", "-noColor", "-method", "Shop.Tests.PriceTests.Costs"], command.Arguments);
    }

    [Fact]
    public void TheLineQuotesWhatAShellWouldSplit()
    {
        TestCommand command = new("/work/my shop", ["test", "Shop.Tests.csproj", "--filter", "FullyQualifiedName=A.B|FullyQualifiedName=A.C"]);

        Assert.Equal("cd '/work/my shop' && dotnet test Shop.Tests.csproj --filter 'FullyQualifiedName=A.B|FullyQualifiedName=A.C'", command.Line);
    }

    public void Dispose() => root.Delete(recursive: true);

    private TestApp App(RunnerMode mode)
    {
        string project = Path.Combine(root.FullName, "tests", "Shop.Tests");
        Directory.CreateDirectory(project);
        return new TestApp(project, Path.Combine(project, "bin", "Debug", "net10.0", "Shop.Tests.dll"), mode) { ProjectFile = Path.Combine(project, "Shop.Tests.csproj") };
    }
}
