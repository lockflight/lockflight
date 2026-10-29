using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Affected;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Affected;

/// <summary>
/// The stop profile's choice of projects on a layered graph, read from project files in a git repository:
/// Core ← Domain ← Api, Core ← Cli (no tests), Core.Tests → Core, Domain.Tests → Domain, Api.Tests → Api.
/// </summary>
public sealed class ProjectSelectorTests : IDisposable
{
    private const string Solution = "Graph.slnx";

    private const string PassingTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results><UnitTestResult testName="Api.Tests.Answers" outcome="Passed" /></Results>
          <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary>
        </TestRun>
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-affected-");

    [Fact]
    public async Task ALeafChangeChoosesItsProjectAndTheTestsThatDependOnIt()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Affected, selection.Kind);
        Assert.Equal(["Api.csproj (changed: src/Api/Api.cs)", "Api.Tests.csproj (affected by src/Api/Api.cs via Api.csproj)"], Described(selection));
        Assert.True(selection.RunsTests);
        Assert.Equal("2 of 7 projects (affected)", selection.Scope);
        string filter = await File.ReadAllTextAsync(selection.FilterFile!, TestContext.Current.CancellationToken);
        Assert.Contains("src/Api/Api.csproj", filter, StringComparison.Ordinal);
        Assert.Contains("tests/Api.Tests/Api.Tests.csproj", filter, StringComparison.Ordinal);
        Assert.DoesNotContain("Core", filter, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeChoosesEveryProjectThatDependsOnItTransitivelyNamingTheProjectItCameThrough()
    {
        await CreateGraphAsync();
        Write("src/Domain/Domain.cs", "class Domain { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(
            [
                "Domain.csproj (changed: src/Domain/Domain.cs)",
                "Api.csproj (affected by src/Domain/Domain.cs via Domain.csproj)",
                "Domain.Tests.csproj (affected by src/Domain/Domain.cs via Domain.csproj)",
                "Api.Tests.csproj (affected by src/Domain/Domain.cs via Api.csproj)",
            ],
            Described(selection));
    }

    [Fact]
    public async Task AChangeEveryProjectDependsOnRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Core/Core.cs", "class Core { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal("the changes affect all 7 projects", selection.Reason);
        Assert.Null(selection.FilterFile);
    }

    [Fact]
    public async Task AChangedProjectWithoutTestsRunsNoTest()
    {
        await CreateGraphAsync();
        Write("src/Cli/Program.cs", "System.Console.WriteLine(2);\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Affected, selection.Kind);
        Assert.Equal(["Cli.csproj (changed: src/Cli/Program.cs)"], Described(selection));
        Assert.False(selection.RunsTests);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData("NuGet.config")]
    [InlineData("build/Common.targets")]
    [InlineData("src/Api/Directory.Build.targets")]
    [InlineData("Graph.slnx")]
    public async Task ASharedBuildInputRunsEverything(string file)
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");
        Write(file, file.EndsWith(".json", StringComparison.Ordinal) ? "{}\n" : "<Project />\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal($"{file} changed, a build input every project may share", selection.Reason);
    }

    [Fact]
    public async Task AChangeOutsideEveryProjectInTheTargetRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");
        Write("docs/notes.md", "notes\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal("docs/notes.md changed outside every project, so which projects use it is not known", selection.Reason);
    }

    [Fact]
    public async Task LockflightsOwnDirectoriesDoNotChooseProjects()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");
        Write("specs/001-orders/tasks.md", "- [x] T001\n");
        Write(".claude/settings.json", "{}\n");
        Write(".specify/memory/constitution.md", "rules\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Affected, selection.Kind);
        Assert.Equal(2, selection.Projects.Count);
    }

    [Fact]
    public async Task OnlyChangesInLockflightsOwnDirectoriesRunEverything()
    {
        await CreateGraphAsync();
        Write("specs/001-orders/tasks.md", "- [x] T001\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal("no project holds the changes since the last PASS", selection.Reason);
    }

    [Fact]
    public async Task ANewUntrackedFileInAProjectChoosesIt()
    {
        await CreateGraphAsync();
        Write("src/Api/Orders/Order.cs", "class Order {}\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("Api.csproj (changed: src/Api/Orders/Order.cs)", Described(selection)[0]);
    }

    [Fact]
    public async Task NothingChangedSinceTheLastPassRunsEverything()
    {
        await CreateGraphAsync();

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal("nothing changed since the last PASS", selection.Reason);
    }

    [Fact]
    public async Task FullAndThePrProfileRunEverything()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection full = await ProjectSelector.SelectAsync(Run() with { FullRun = true }, TestContext.Current.CancellationToken);
        ProjectSelection pr = await ProjectSelector.SelectAsync(Run() with { Profile = GateProfile.PullRequest }, TestContext.Current.CancellationToken);

        Assert.Equal("--full", full.Reason);
        Assert.Equal(SelectionKind.Everything, pr.Kind);
    }

    [Fact]
    public async Task AReferenceThroughAnMSBuildPropertyRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.csproj", Project("$(RepoRoot)/src/Domain/Domain.csproj"));
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.StartsWith("src/Api/Api.csproj includes '$(RepoRoot)/src/Domain/Domain.csproj'", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReferenceThroughTheProjectsOwnDirectoryIsRead()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.csproj", Project("$(MSBuildThisFileDirectory)../Domain/Domain.csproj"));
        await CommitAsync();
        Write("src/Domain/Domain.cs", "class Domain { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Contains("Api.csproj (affected by src/Domain/Domain.cs via Domain.csproj)", Described(selection));
    }

    [Fact]
    public async Task ADirectoryBuildFileThatAddsReferencesRunsEverything()
    {
        await CreateGraphAsync();
        Write("tests/Directory.Build.targets", "<Project><ItemGroup><ProjectReference Include=\"../src/Core/Core.csproj\" /></ItemGroup></Project>\n");
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("tests/Directory.Build.targets adds project references, which the project files alone do not show", selection.Reason);
    }

    [Fact]
    public async Task AnUnreadableProjectFileRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n");
        Write("src/Domain/Domain.cs", "class Domain { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.StartsWith("src/Api/Api.csproj could not be read", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingReferencedProjectRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.csproj", Project("../Gone/Gone.csproj"));
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("src/Api/Api.csproj references '../Gone/Gone.csproj', which is not one existing project file", selection.Reason);
    }

    [Fact]
    public async Task AFileLinkedFromOutsideAProjectChangesTheProjectThatIncludesIt()
    {
        await CreateGraphAsync();
        Write("src/Cli/Cli.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <ProjectReference Include=\"../Core/Core.csproj\" />\n    <Compile Include=\"../../shared/Clock.cs\" Link=\"Clock.cs\" />\n  </ItemGroup>\n</Project>\n");
        Write("shared/Clock.cs", "class Clock {}\n");
        await CommitAsync();
        Write("shared/Clock.cs", "class Clock { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(["Cli.csproj (changed: shared/Clock.cs)"], Described(selection));
    }

    [Fact]
    public async Task AChangeInAnotherUnitDoesNotChooseProjects()
    {
        await CreateGraphAsync();
        Write("services/billing/Billing.cs", "class Billing {}\n");
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await ProjectSelector.SelectAsync(
            Run() with { UnitDirectories = [Path.Combine(root.FullName, "services", "billing")] }, TestContext.Current.CancellationToken);

        Assert.Equal(SelectionKind.Affected, selection.Kind);
        Assert.Equal(2, selection.Projects.Count);
    }

    [Fact]
    public async Task ATargetWithoutASingleSolutionRunsEverything()
    {
        await CreateGraphAsync();
        Write("Other.sln", string.Empty);
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("the target has no single solution file", selection.Reason);
    }

    [Fact]
    public async Task ATestFrameworkNamedInADirectoryBuildFileRunsEverythingWhenNoAffectedProjectDeclaresTests()
    {
        await CreateGraphAsync();
        Write("tests/Directory.Build.props", "<Project><ItemGroup><PackageReference Include=\"xunit.v3\" /></ItemGroup></Project>\n");
        await CommitAsync();
        Write("src/Cli/Program.cs", "System.Console.WriteLine(2);\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.StartsWith("no affected project declares tests", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClassicSolutionIsReadWithoutItsSolutionFolders()
    {
        await CreateGraphAsync();
        File.Delete(PathOf(Solution));
        Write("Graph.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Core", "src\Core\Core.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Api", "src\Api\Api.csproj", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            """);

        IReadOnlyList<string>? entries = SolutionEntries.Read(PathOf("Graph.sln"));

        Assert.Equal([@"src\Core\Core.csproj", @"src\Api\Api.csproj"], entries);
    }

    [Fact]
    public async Task TheBuildAndTestStepsRunTheChosenProjectsThroughTheSolutionFilter()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");
        List<string> calls = [];
        GateRunContext run = Run() with
        {
            Processes = (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
            {
                calls.Add(string.Join(' ', arguments));
                return Task.FromResult(new ProcessResult(0, "Build succeeded.", string.Empty, 0, TimedOut: false));
            },
        };

        GateRunFacts facts = GateRunFacts.None;
        foreach (IGateStep step in GateProfiles.For(GateProfile.Stop).PerUnit)
        {
            facts = (await step.RunAsync(run, facts, TestContext.Current.CancellationToken)).Facts;
        }

        string filter = StopPassRecord.PathOf(root.FullName, root.FullName, ".slnf");
        Assert.Equal(filter, facts.Selection?.FilterFile);
        Assert.StartsWith($"build {filter} ", calls[0], StringComparison.Ordinal);
        Assert.StartsWith($"test {filter} --no-build", calls[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFilterNamesTheSolutionByItsRealPath()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        await CreateGraphAsync();
        string link = Path.Combine(Path.GetTempPath(), $"lockflight-affected-link-{Guid.NewGuid():N}");
        Directory.CreateSymbolicLink(link, root.FullName);
        try
        {
            Write("src/Api/Api.cs", "class Api { int changed; }\n");

            ProjectSelection selection = await ProjectSelector.SelectAsync(
                new GateRunContext(link, link, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System), TestContext.Current.CancellationToken);

            string filter = await File.ReadAllTextAsync(selection.FilterFile!, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(link, filter, StringComparison.Ordinal);
            Assert.Contains("Graph.slnx", filter, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task ACommitSinceTheLastPassStillChoosesTheProjectsItChanged()
    {
        await CreateGraphAsync();
        Write("src/Domain/Domain.cs", "class Domain { int committedWithoutAPass; }\n");
        await CommitWithoutPassAsync();
        Write("src/Cli/Program.cs", "System.Console.WriteLine(2);\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Contains("Domain.Tests.csproj (affected by src/Domain/Domain.cs via Domain.csproj)", Described(selection));
        Assert.Contains("Cli.csproj (changed: src/Cli/Program.cs)", Described(selection));
    }

    [Fact]
    public async Task AnEditUndoneSinceTheLastPassIsAChangeToo()
    {
        await CreateGraphAsync();
        Write("src/Api/Api.cs", "class Api { int passedWithThis; }\n");
        await PassAsync();
        Write("src/Api/Api.cs", "class Api {}\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(["Api.csproj (changed: src/Api/Api.cs)", "Api.Tests.csproj (affected by src/Api/Api.cs via Api.csproj)"], Described(selection));
    }

    [Fact]
    public async Task WithoutARecordedPassEverythingRuns()
    {
        await CreateGraphAsync();
        File.Delete(StopPassRecord.PathOf(root.FullName, root.FullName, ".pass"));
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.StartsWith("no stop PASS of this target", selection.Reason, StringComparison.Ordinal);
        Assert.NotNull(selection.PassRecord);
    }

    [Fact]
    public async Task ProjectsThatShareADirectoryAreBothChanged()
    {
        await CreateGraphAsync();
        Write("src/Api/Api2.csproj", Project("../Domain/Domain.csproj"));
        Write(Solution, File.ReadAllText(PathOf(Solution)).Replace("</Solution>", "  <Project Path=\"src/Api/Api2.csproj\" />\n</Solution>", StringComparison.Ordinal));
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Contains("Api2.csproj (changed: src/Api/Api.cs)", Described(selection));
    }

    [Fact]
    public async Task AnOuterProjectOwnsTheFilesOfAProjectNestedInIt()
    {
        await CreateGraphAsync();
        Write("src/Cli/Plugins/Plugin.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write(Solution, File.ReadAllText(PathOf(Solution)).Replace("</Solution>", "  <Project Path=\"src/Cli/Plugins/Plugin.csproj\" />\n</Solution>", StringComparison.Ordinal));
        await CommitAsync();
        Write("src/Cli/Plugins/Plugin.cs", "class Plugin {}\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Contains("Cli.csproj (changed: src/Cli/Plugins/Plugin.cs)", Described(selection));
        Assert.Contains("Plugin.csproj (changed: src/Cli/Plugins/Plugin.cs)", Described(selection));
    }

    [Fact]
    public async Task ATestFrameworkInAnImportedFileDeclaresTests()
    {
        await CreateGraphAsync();
        Write("build/tests.props", "<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>\n");
        Write("tests/Api.Tests/Api.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <Import Project=\"../../build/tests.props\" />\n  <ItemGroup>\n    <ProjectReference Include=\"../../src/Api/Api.csproj\" />\n  </ItemGroup>\n</Project>\n");
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.True(selection.RunsTests);
        Assert.True(selection.Projects.Single(project => project.Name == "Api.Tests.csproj").DeclaresTests);
    }

    [Fact]
    public async Task AFileImportedByADirectoryBuildFileThatAddsReferencesRunsEverything()
    {
        await CreateGraphAsync();
        Write("Directory.Build.props", "<Project><Import Project=\"build/analyzers.props\" /></Project>\n");
        Write("build/analyzers.props", "<Project><ItemGroup><ProjectReference Include=\"$(MSBuildThisFileDirectory)../src/Core/Core.csproj\" OutputItemType=\"Analyzer\" /></ItemGroup></Project>\n");
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("build/analyzers.props adds project references, which the project files alone do not show", selection.Reason);
    }

    [Fact]
    public async Task ADirectoryBuildFileAboveTheRepositoryRunsEverything()
    {
        await CreateGraphAsync();
        DirectoryInfo shell = Directory.CreateTempSubdirectory("lockflight-affected-shell-");
        string unit = Path.Combine(shell.FullName, "unit");
        Directory.Move(root.FullName, unit);
        try
        {
            File.WriteAllText(Path.Combine(shell.FullName, "Directory.Build.props"), "<Project />\n");
            Write(unit, "src/Api/Api.cs", "class Api { int changed; }\n");

            ProjectSelection selection = await ProjectSelector.SelectAsync(
                new GateRunContext(unit, unit, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System), TestContext.Current.CancellationToken);

            Assert.Equal(SelectionKind.Everything, selection.Kind);
        }
        finally
        {
            Directory.Move(unit, root.FullName);
            TempDirectories.DeleteRecursively(shell);
        }
    }

    [Fact]
    public async Task OnlyAPassingStopRunRecordsTheTreeTheNextRunComparesWith()
    {
        await CreateGraphAsync();
        string record = StopPassRecord.PathOf(root.FullName, root.FullName, ".pass");
        string greenBase = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        await GateRunner.RunAsync(Run() with { Processes = ToolsExiting(1) }, TextWriter.Null, TestContext.Current.CancellationToken);
        string afterFailure = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
        await GateRunner.RunAsync(Run() with { Processes = ToolsExiting(0) }, TextWriter.Null, TestContext.Current.CancellationToken);
        string afterPass = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);

        Assert.Equal(greenBase, afterFailure);
        Assert.NotEqual(greenBase, afterPass);
    }

    [Fact]
    public async Task AGitIgnoredImportRunsEverything()
    {
        await CreateGraphAsync();
        Write("Directory.Build.props", "<Project><Import Project=\"local.props\" Condition=\"Exists('local.props')\" /></Project>\n");
        Write(".gitignore", ".lockflight/local/\nlocal.props\n");
        Write("local.props", "<Project />\n");
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("local.props is git-ignored, so its changes are not seen", selection.Reason);
    }

    [Fact]
    public async Task AnEntryGitDoesNotReadAgainRunsEverything()
    {
        await CreateGraphAsync();
        await GitAsync(["update-index", "--skip-worktree", "src/Core/Core.cs"]);
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("src/Core/Core.cs is marked assume-unchanged or skip-worktree, so git does not read its changes", selection.Reason);
    }

    [Fact]
    public async Task AFileLinkedFromOutsideTheRepositoryRunsEverything()
    {
        await CreateGraphAsync();
        Write("src/Cli/Cli.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <Compile Include=\"../../../shared/Clock.cs\" />\n  </ItemGroup>\n</Project>\n");
        await CommitAsync();
        Write("src/Api/Api.cs", "class Api { int changed; }\n");

        ProjectSelection selection = await SelectAsync();

        Assert.Equal("src/Cli/Cli.csproj includes '../../../shared/Clock.cs', outside this repository, whose git does not see its changes", selection.Reason);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    /// <summary>Every tool exits with the code; a passing test run writes a TRX report of one passed test.</summary>
    private static RunTimedProcess ToolsExiting(int exitCode) => (fileName, arguments, workingDirectory, timeLimit, cancellationToken) =>
    {
        int results = arguments.ToList().IndexOf("--results-directory");
        if (exitCode == 0 && results >= 0)
        {
            Directory.CreateDirectory(arguments[results + 1]);
            File.WriteAllText(Path.Combine(arguments[results + 1], "lockflight.trx"), PassingTrx);
        }

        return Task.FromResult(new ProcessResult(exitCode, exitCode == 0 ? "Build succeeded." : "Api.cs(1,1): error CS1002: ; expected", string.Empty, 0, TimedOut: false));
    };

    private static string[] Described(ProjectSelection selection) => [.. selection.Projects.Select(project => $"{project.Name} ({project.Reason})")];

    private static string Project(params string[] references) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n{string.Concat(references.Select(reference => $"    <ProjectReference Include=\"{reference}\" />\n"))}  </ItemGroup>\n</Project>\n";

    private static string TestProject(string reference) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" />\n    <ProjectReference Include=\"{reference}\" />\n  </ItemGroup>\n</Project>\n";

    private Task<ProjectSelection> SelectAsync() => ProjectSelector.SelectAsync(Run(), TestContext.Current.CancellationToken);

    private GateRunContext Run() => new(root.FullName, root.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);

    private async Task CreateGraphAsync()
    {
        Write("src/Core/Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write("src/Core/Core.cs", "class Core {}\n");
        Write("src/Domain/Domain.csproj", Project("../Core/Core.csproj"));
        Write("src/Domain/Domain.cs", "class Domain {}\n");
        Write("src/Api/Api.csproj", Project("../Domain/Domain.csproj"));
        Write("src/Api/Api.cs", "class Api {}\n");
        Write("src/Cli/Cli.csproj", Project("../Core/Core.csproj"));
        Write("src/Cli/Program.cs", "System.Console.WriteLine(1);\n");
        Write("tests/Core.Tests/Core.Tests.csproj", TestProject("../../src/Core/Core.csproj"));
        Write("tests/Domain.Tests/Domain.Tests.csproj", TestProject("../../src/Domain/Domain.csproj"));
        Write("tests/Api.Tests/Api.Tests.csproj", TestProject("../../src/Api/Api.csproj"));
        string[] projects = ["src/Core/Core.csproj", "src/Domain/Domain.csproj", "src/Api/Api.csproj", "src/Cli/Cli.csproj", "tests/Core.Tests/Core.Tests.csproj", "tests/Domain.Tests/Domain.Tests.csproj", "tests/Api.Tests/Api.Tests.csproj"];
        Write(Solution, $"<Solution>\n{string.Concat(projects.Select(project => $"  <Project Path=\"{project}\" />\n"))}</Solution>\n");
        Write(".gitignore", ".lockflight/local/\n");
        await GitAsync(["init", "-q"]);
        await CommitAsync();
    }

    /// <summary>Commits everything, as a stop PASS of that tree would have recorded it.</summary>
    private async Task CommitAsync()
    {
        await CommitWithoutPassAsync();
        await PassAsync();
    }

    private async Task CommitWithoutPassAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-qm", "graph"]);
    }

    /// <summary>What a stop PASS of the tree as it is now records.</summary>
    private async Task PassAsync()
    {
        ProjectSelection selection = await ProjectSelector.SelectAsync(Run() with { FullRun = true }, TestContext.Current.CancellationToken);
        await selection.PassRecord!.RememberAsync(TestContext.Current.CancellationToken);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult git = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(git.ExitCode == 0, git.Combined);
    }

    private string PathOf(string relative) => Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void Write(string directory, string relative, string content)
    {
        string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void Write(string relative, string content)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
