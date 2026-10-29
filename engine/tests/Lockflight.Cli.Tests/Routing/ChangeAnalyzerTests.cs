using Lockflight.Cli.Routing;
using Lockflight.Cli.Tests.Review;

namespace Lockflight.Cli.Tests.Routing;

/// <summary>
/// Table-driven: each case is a base tree, the edits a pull request makes, and every changed file's expected role, counted lines and
/// move. Each case runs twice — committed (CI reads the head commit) and uncommitted (a local run reads the working tree with its
/// untracked files) — and both must read the same change.
/// </summary>
public sealed class ChangeAnalyzerTests
{
    private const string ProductionProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";
    private const string TestProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"3.0.0\" />\n  </ItemGroup>\n</Project>\n";
    private const string Service = "namespace Shop;\n\npublic sealed class Service\n{\n    public int Total(int a, int b)\n    {\n        return a + b;\n    }\n\n    // Adds the tax.\n    public int Tax(int amount) => amount / 5;\n}\n";

    public static TheoryData<string> Cases => [.. AllCases.Keys];

    private static Dictionary<string, RoutingCase> AllCases { get; } = new RoutingCase[]
    {
        new("ABodyEditCountsItsLines", App(), repo => repo.Write("src/App/Service.cs", Service.Replace("a + b", "a + b + 1", StringComparison.Ordinal)),
            ["src/App/Service.cs Production 2"]),
        new("AReindentCountsNothing", App(), repo => repo.Write("src/App/Service.cs", Service.Replace("        return", "\treturn", StringComparison.Ordinal)),
            ["src/App/Service.cs Production 0"]),
        new("LineEndingsAloneCountNothing", App(), repo => repo.Write("src/App/Service.cs", Service.Replace("\n", "\r\n", StringComparison.Ordinal)),
            ["src/App/Service.cs Production 0"]),
        new("ACommentOnlyEditCountsNothing", App(), repo => repo.Write("src/App/Service.cs", Service.Replace("Adds the tax.", "Adds the sales tax.", StringComparison.Ordinal)),
            ["src/App/Service.cs Production 0"]),
        new("ALineAddedInsideABlockCommentCountsNothing", App(("src/App/Notes.cs", "/*\n notes\n*/\nclass Notes { }\n")),
            repo => repo.Write("src/App/Notes.cs", "/*\n notes\n public class Hidden { }\n*/\nclass Notes { }\n"),
            ["src/App/Notes.cs Production 0"]),
        new("AStringLiteralEditCounts", App(("src/App/Text.cs", "class Text { string Hello = \"hello\"; }\n")),
            repo => repo.Write("src/App/Text.cs", "class Text { string Hello = \"world\"; }\n"),
            ["src/App/Text.cs Production 2"]),
        new("ADeletedFileKeepsItsRemovedLines", App(), repo => repo.Delete("src/App/Service.cs"),
            ["src/App/Service.cs Production 9"]),
        new("AnExactMoveCountsNothing", App(), repo => repo.Move("src/App/Service.cs", "src/App/Core/Service.cs"),
            ["src/App/Core/Service.cs Production 0 Exact", "src/App/Service.cs Production 0 Exact"]),
        new("AnEditedMoveCountsOnlyTheDifferingLines", App(),
            repo => repo.Delete("src/App/Service.cs").Write("src/App/Core/Service.cs", Service.Replace("namespace Shop;", "namespace Shop.Core;", StringComparison.Ordinal)),
            ["src/App/Core/Service.cs Production 2 Edited", "src/App/Service.cs Production 0 Edited"]),
        new("AMoveFromATestProjectIntoProductionIsNotAMove", App(("tests/App.Tests/App.Tests.csproj", TestProject), ("tests/App.Tests/Helper.cs", "class Helper\n{\n    int Value => 1;\n}\n")),
            repo => repo.Move("tests/App.Tests/Helper.cs", "src/App/Helper.cs"),
            ["src/App/Helper.cs Production 4", "tests/App.Tests/Helper.cs Test 4"]),
        new("AMoveBetweenProjectsIsNotAMove", App(("src/Other/Other.csproj", ProductionProject)),
            repo => repo.Move("src/App/Service.cs", "src/Other/Service.cs"),
            ["src/App/Service.cs Production 9", "src/Other/Service.cs Production 9"]),
        new("IdenticalFilesAreNeverPaired", App(("src/App/A.cs", "class Same { }\n"), ("src/App/B.cs", "class Same { }\n")),
            repo => repo.Move("src/App/A.cs", "src/App/X/A.cs").Move("src/App/B.cs", "src/App/X/B.cs"),
            ["src/App/A.cs Production 1", "src/App/B.cs Production 1", "src/App/X/A.cs Production 1", "src/App/X/B.cs Production 1"]),
        new("AProjectFolderRenameKeepsItsFilesAsMoves", App(),
            repo => repo.Move("src/App/App.csproj", "src/Shop/Shop.csproj").Move("src/App/Service.cs", "src/Shop/Service.cs"),
            ["src/App/App.csproj Project 0 Exact", "src/App/Service.cs Production 0 Exact", "src/Shop/Service.cs Production 0 Exact", "src/Shop/Shop.csproj Project 0 Exact"]),
        new("ATestPackageAddedToAProductionProjectLeavesItsFilesProduction", App(),
            repo => repo.Write("src/App/App.csproj", TestProject).Write("src/App/Service.cs", Service.Replace("a + b", "a - b", StringComparison.Ordinal)),
            ["src/App/App.csproj Project 6", "src/App/Service.cs Production 2"]),
        new("ATestProjectIsFoundThroughDirectoryBuildProps",
            App(("tests/Directory.Build.props", TestProject), ("tests/App.Tests/App.Tests.csproj", ProductionProject), ("tests/App.Tests/ServiceTests.cs", "class ServiceTests { }\n")),
            repo => repo.Write("tests/App.Tests/ServiceTests.cs", "class ServiceTests { int Count; }\n"),
            ["tests/App.Tests/ServiceTests.cs Test 2"]),
        new("ACommentedOutTestPackageDoesNotMakeATestProject",
            App(("src/Fake/Fake.csproj", "<Project>\n<!-- <PackageReference Include=\"xunit\" /> -->\n</Project>\n"), ("src/Fake/Code.cs", "class Code { }\n")),
            repo => repo.Write("src/Fake/Code.cs", "class Code { int Value; }\n"),
            ["src/Fake/Code.cs Production 2"]),
        new("UpperCaseNamesHaveTheirRoles", App(("LIBS/CORE/CORE.CSPROJ", ProductionProject), ("LIBS/CORE/CODE.CS", "class Code { }\n")),
            repo => repo.Write("LIBS/CORE/CODE.CS", "class Code { int Value; }\n").Write("DIRECTORY.BUILD.PROPS", "<Project />\n").Write("README.MD", "# Shop\n"),
            ["DIRECTORY.BUILD.PROPS BuildWide 1", "LIBS/CORE/CODE.CS Production 2", "README.MD Docs 1"]),
        new("ACSharpFileUnderNoProjectIsProduction", App(), repo => repo.Write("tools/Build.cs", "class Build { }\n"),
            ["tools/Build.cs Production 1"]),
        new("ABinaryFileCountsNothing", App(), repo => repo.WriteBytes("src/App/logo.bin", [1, 0, 2, 0, 3]),
            ["src/App/logo.bin ProductionConfig 0 Binary"]),
        new("GeneratedFilesAreFoundByPathAndShape", App(("src/App/Migrations/ShopContextModelSnapshot.cs", EfSamples.Snapshot)),
            repo => repo
                .Write("src/App/Migrations/ShopContextModelSnapshot.cs", EfSamples.Snapshot.Replace("b.HasKey(\"Id\");", "b.HasKey(\"Id\");\n                    b.HasIndex(\"Total\");", StringComparison.Ordinal))
                .Write("src/App/packages.lock.json", "{}\n")
                .Write("src/App/Migrations/FakeModelSnapshot.cs", "class FakeModelSnapshot { void Pay() { } }\n"),
            ["src/App/Migrations/FakeModelSnapshot.cs Production 1", "src/App/Migrations/ShopContextModelSnapshot.cs Generated 1", "src/App/packages.lock.json Generated 1"]),
        new("BaseExclusionGlobsMakeNonProduction", App(("src/App/Clients/Client.cs", "class Client { }\n")),
            repo => repo.Write("src/App/Clients/Client.cs", "class Client { int Value; }\n"),
            ["src/App/Clients/Client.cs NonProduction 2"], Excluded: ["src/*/Clients/**"]),
        new("EveryOtherKindOfFileHasItsRole", App(("src/Fs/Fs.fsproj", ProductionProject)),
            repo => repo
                .Write("src/App/Page.razor", "@page \"/\"\n")
                .Write("src/App/appsettings.json", "{}\n")
                .Write("src/App/App.sln", "Microsoft Visual Studio Solution File\n")
                .Write("src/Fs/Library.fs", "module Library\n")
                .Write("coverage.runsettings", "<RunSettings />\n")
                .Write("global.json", "{}\n")
                .Write("Dockerfile", "FROM scratch\n")
                .Write("docs/notes.txt", "notes\n")
                .Write(".lockflight/features/001-cart/redgreen.json", "{}\n"),
            [
                ".lockflight/features/001-cart/redgreen.json Docs 1", "Dockerfile Other 1", "coverage.runsettings TestConfig 1", "docs/notes.txt Docs 1",
                "global.json BuildWide 1", "src/App/App.sln Project 1", "src/App/Page.razor Markup 1", "src/App/appsettings.json ProductionConfig 1",
                "src/Fs/Library.fs Production 1",
            ]),
    }.ToDictionary(routingCase => routingCase.Name, StringComparer.Ordinal);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CommittedChangesAreReadAsTheCaseExpects(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RoutingCase routingCase = AllCases[name];
        using RoutingRepository repository = await RoutingRepository.CreateAsync(routingCase.BaseFiles, cancellationToken);
        routingCase.Edit(repository);
        await repository.CommitAsync("change", cancellationToken);

        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, "HEAD", cancellationToken);

        Assert.Equal(routingCase.Expected, Describe(reading, routingCase.Excluded));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task UncommittedChangesAreReadAsTheSameChange(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RoutingCase routingCase = AllCases[name];
        using RoutingRepository repository = await RoutingRepository.CreateAsync(routingCase.BaseFiles, cancellationToken);
        routingCase.Edit(repository);

        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, headRef: null, cancellationToken);

        Assert.Equal(routingCase.Expected, Describe(reading, routingCase.Excluded));
    }

    private static List<string> Describe(ChangeSetReading reading, IReadOnlyList<string> excluded)
    {
        Assert.True(reading.Status == ChangeSetReadStatus.Read, reading.Message);
        ChangeAnalysis analysis = ChangeAnalyzer.Analyze(reading.ChangeSet!, excluded);
        return [.. analysis.Files.Select(file =>
            $"{file.Path} {file.Role} {file.CountedLines}{(file.Move is null ? string.Empty : " " + file.Move.Kind)}{(file.Change.Binary ? " Binary" : string.Empty)}")];
    }

    private static Dictionary<string, string> App(params (string Path, string Content)[] more)
    {
        Dictionary<string, string> files = new(StringComparer.Ordinal)
        {
            ["src/App/App.csproj"] = ProductionProject,
            ["src/App/Service.cs"] = Service,
        };
        foreach ((string path, string content) in more)
        {
            files[path] = content;
        }

        return files;
    }

    private sealed record RoutingCase(string Name, IReadOnlyDictionary<string, string> BaseFiles, Action<RoutingRepository> Edit, IReadOnlyList<string> Expected, IReadOnlyList<string>? Excluded = null)
    {
        public IReadOnlyList<string> Excluded { get; } = Excluded ?? [];
    }
}
