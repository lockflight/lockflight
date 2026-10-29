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
        Write(file, "<Project />\n");

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
        Assert.Equal("no project holds the uncommitted changes", selection.Reason);
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
    public async Task NoUncommittedChangeRunsEverything()
    {
        await CreateGraphAsync();

        ProjectSelection selection = await SelectAsync();

        Assert.Equal(SelectionKind.Everything, selection.Kind);
        Assert.Equal("no uncommitted changes to choose projects by", selection.Reason);
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

        string filter = SolutionFilter.PathFor(root.FullName, unitPath: null);
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

    public void Dispose() => TempDirectories.DeleteRecursively(root);

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

    private async Task CommitAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "-qm", "graph"]);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult git = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(git.ExitCode == 0, git.Combined);
    }

    private string PathOf(string relative) => Path.Combine(root.FullName, relative.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relative, string content)
    {
        string path = PathOf(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
