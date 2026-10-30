using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ProjectFileLinesTests
{
    [Theory]
    [InlineData("<PackageReference Include=\"Serilog\" Version=\"4.0.0\" />", "<PackageReference Include=\"Serilog\" Version=\"4.1.0\" />", "Package Package")]
    [InlineData("<Version>1.2.0</Version>", "<Version>1.3.0</Version>", "Package Package")]
    [InlineData("<AssemblyVersion>1.0.0.0</AssemblyVersion>", "<AssemblyVersion>2.0.0.0</AssemblyVersion>", "Package Package")]
    [InlineData("<PackageReference Include=\"Serilog\" Version=\"4.0.0\" />", "<PackageReference Include=\"Serilog.Sinks\" Version=\"4.0.0\" />", "Package Package")]
    [InlineData("<Nullable>enable</Nullable>", "<Nullable>disable</Nullable>", "Other Other")]
    [InlineData("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", "<TreatWarningsAsErrors>false</TreatWarningsAsErrors>", "PolicyProperty PolicyProperty")]
    [InlineData("<Import Project=\"a.targets\" />", "<Import Project=\"b.targets\" />", "PolicyProperty PolicyProperty")]
    [InlineData("<Compile Include=\"Local.cs\" />", "<Compile Include=\"../Other/Shared.cs\" />", "Other PolicyProperty")]
    [InlineData("<ProjectReference Include=\"../A/A.csproj\" />", "<ProjectReference Include=\"../B/B.csproj\" />", "Reference Reference")]
    [InlineData("<ItemGroup>", "<ItemGroup Condition=\"'$(X)' == 'y'\">", "Other")]
    [InlineData("<!-- a note -->", "", "")]
    public void ChangedLinesAreSortedByWhatTheyDo(string removed, string added, string expected)
    {
        RoutedLine[] removedLines = removed.Length == 0 ? [] : [new RoutedLine(1, removed, removed)];
        RoutedLine[] addedLines = added.Length == 0 ? [] : [new RoutedLine(1, added, added)];

        IReadOnlyList<ProjectLine> lines = ProjectFileLines.Of(addedLines, removedLines);

        Assert.Equal(expected, string.Join(' ', lines.Select(line => line.Kind)));
    }

    [Theory]
    [InlineData("      - uses: actions/checkout@v4", "      - uses: actions/checkout@v5", true)]
    [InlineData("      - uses: actions/checkout@0123abc # v4", "      - uses: actions/checkout@4567def # v5", true)]
    [InlineData("    image: mcr.microsoft.com/dotnet/sdk:9.0", "    image: mcr.microsoft.com/dotnet/sdk:10.0", true)]
    [InlineData("      - uses: actions/checkout@v4", "      - uses: evil/checkout@v4", false)]
    [InlineData("      - run: dotnet test", "      - run: dotnet test --filter X", false)]
    public void CiPinBumpsAreTheOnlyVersionOnlyEdits(string removed, string added, bool versionOnly) =>
        Assert.Equal(versionOnly, CiFiles.WithoutPins(removed) == CiFiles.WithoutPins(added));

    [Theory]
    [InlineData("<ProjectReference Include=\"..\\Billing\\Billing.csproj\" />", "src/Billing/Billing.csproj")]
    [InlineData("<ProjectReference Include=\"../../tests/T/T.csproj\" />", "tests/T/T.csproj")]
    [InlineData("<ProjectReference\n    Include=\"../Billing/Billing.csproj\" />", "src/Billing/Billing.csproj")]
    [InlineData("<ProjectReference Include=\"../A/A.csproj;../B/B.csproj\" />", "src/A/A.csproj|src/B/B.csproj")]
    [InlineData("<ProjectReference Include=\"$(MSBuildThisFileDirectory)../Billing/Billing.csproj\" />", "src/Billing/Billing.csproj")]
    [InlineData("<ProjectReference Include=\"$(Root)/Billing.csproj\" />", "-")]
    [InlineData("<ProjectReference Include=\"../../../Outside.csproj\" />", "-")]
    [InlineData("<!-- <ProjectReference Include=\"../A/A.csproj\" /> -->", "")]
    public void ProjectReferencesResolveFromTheReferencingProject(string text, string expected) =>
        Assert.Equal(expected, string.Join('|', ProjectReferences.Of(text, "src/Shop/Shop.csproj").Select(reference => reference.Target ?? "-")));

    [Theory]
    [InlineData("<!-- a --><TreatWarningsAsErrors>false</TreatWarningsAsErrors><!-- b -->", "PolicyProperty")]
    [InlineData("<PackageVersion Include=\"A\" Version=\"2\" /></ItemGroup><PropertyGroup><X>1</X></PropertyGroup><ItemGroup>", "Other")]
    [InlineData("<GlobalPackageReference Include=\"StyleCop.Analyzers\" Version=\"1\" />", "PolicyProperty")]
    [InlineData("<PackageReference Include=\"A\" Version=\"1\" ExcludeAssets=\"analyzers\" />", "PolicyProperty")]
    [InlineData("<Target Name=\"Hide\" />", "PolicyProperty")]
    [InlineData("<!-- a --><!-- b -->", "")]
    public void AnAddedLineIsJudgedByEverythingOnIt(string added, string expected) =>
        Assert.Equal(expected, string.Join(' ', ProjectFileLines.Of([new RoutedLine(1, added, added)], []).Select(line => line.Kind)));

    [Theory]
    [InlineData(".github/workflows/build.yml", true)]
    [InlineData(".GitHub/Workflows/Build.yml", true)]
    [InlineData(".gitlab-ci.yml", true)]
    [InlineData("azure-pipelines.release.yml", true)]
    [InlineData("deploy/build.yml", false)]
    public void CiFilesAreKnownByPath(string path, bool isCi) => Assert.Equal(isCi, CiFiles.IsCiFile(path));
}
