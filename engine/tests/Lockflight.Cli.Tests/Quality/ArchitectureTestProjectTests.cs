using System.Xml.Linq;
using Lockflight.Cli.Quality;

namespace Lockflight.Cli.Tests.Quality;

public sealed class ArchitectureTestProjectTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-architecture-");

    [Fact]
    public void LayersAreFoundByProjectNameInnerFirstAndTestProjectsAreNotLayers()
    {
        Project("src/Shop.Api/Shop.Api.csproj");
        Project("src/Shop.Domain/Shop.Domain.csproj", "<AssemblyName>Shop.Core</AssemblyName>");
        Project("src/Shop.Infrastructure/Shop.Infrastructure.csproj");
        Project("tests/Shop.Domain.Tests/Shop.Domain.Tests.csproj", "<PackageReference Include=\"xunit.v3\" />");
        Project("tests/Domain/Domain.csproj", "<PackageReference Include=\"xunit.v3\" />");

        IReadOnlyList<LayerProject> layers = ArchitectureTestProject.Layers(root.FullName);

        Assert.Equal(
            [(ArchitectureLayer.Domain, "src/Shop.Domain/Shop.Domain.csproj", "Shop.Core"), (ArchitectureLayer.Infrastructure, "src/Shop.Infrastructure/Shop.Infrastructure.csproj", "Shop.Infrastructure"), (ArchitectureLayer.Api, "src/Shop.Api/Shop.Api.csproj", "Shop.Api")],
            layers.Select(layer => (layer.Layer, layer.RelativePath, layer.AssemblyName)));
        Assert.Null(ArchitectureTestProject.Problem(layers));
        Assert.Equal("Shop.ArchitectureTests", ArchitectureTestProject.Name(layers));
    }

    [Fact]
    public void OneLayerHasNothingToTest()
    {
        Project("src/Shop.Domain/Shop.Domain.csproj");

        Assert.Contains("fewer than two layer projects", ArchitectureTestProject.Problem(ArchitectureTestProject.Layers(root.FullName)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheTestChecksOnlyTheLayersThatExistEachAgainstTheLayersOutsideIt()
    {
        Project("src/Shop.Domain/Shop.Domain.csproj");
        Project("src/Shop.Infrastructure/Shop.Infrastructure.csproj");

        string test = ArchitectureTestProject.TestFile(ArchitectureTestProject.Layers(root.FullName));

        Assert.Contains("namespace Shop.ArchitectureTests;", test, StringComparison.Ordinal);
        Assert.Contains("ReflectionAssembly.Load(\"Shop.Domain\")", test, StringComparison.Ordinal);
        Assert.Contains("Types().That().Are(Domain).Should().NotDependOnAny(Infrastructure).Check(Architecture);", test, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplicationAssembly", test, StringComparison.Ordinal);
        Assert.DoesNotContain("InfrastructureDependsOnNoOuterLayer", test, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoProjectsOfOneLayerFormOneTypeSet()
    {
        Project("src/Orders.Domain/Orders.Domain.csproj");
        Project("src/Billing.Domain/Billing.Domain.csproj");
        Project("src/Shop.Api/Shop.Api.csproj");

        string test = ArchitectureTestProject.TestFile(ArchitectureTestProject.Layers(root.FullName));

        Assert.Contains("Types().That().ResideInAssembly(DomainAssembly).Or().ResideInAssembly(DomainAssembly2).As(\"Domain\")", test, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectReferencesEveryLayerAndUsesTheRepositorysTestPlatform()
    {
        Project("src/Shop.Domain/Shop.Domain.csproj", "<TargetFramework>net8.0</TargetFramework>");
        Project("src/Shop.Api/Shop.Api.csproj", "<TargetFramework>net9.0</TargetFramework>");
        IReadOnlyList<LayerProject> layers = ArchitectureTestProject.Layers(root.FullName);
        string directory = ArchitectureTestProject.RelativeDirectory(root.FullName, layers);

        XDocument vstest = XDocument.Parse(ArchitectureTestProject.ProjectFile(root.FullName, directory, layers, centralPackages: false));
        File.WriteAllText(Path.Combine(root.FullName, "global.json"), """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        XDocument platform = XDocument.Parse(ArchitectureTestProject.ProjectFile(root.FullName, directory, layers, centralPackages: true));

        Assert.Equal("tests/Shop.ArchitectureTests", directory);
        Assert.Equal("net9.0", vstest.Descendants("TargetFramework").Single().Value);
        Assert.Equal(["../../src/Shop.Domain/Shop.Domain.csproj", "../../src/Shop.Api/Shop.Api.csproj"], vstest.Descendants("ProjectReference").Select(reference => reference.Attribute("Include")!.Value));
        Assert.Contains("xunit.runner.visualstudio", Packages(vstest));
        Assert.Contains(ArchitectureTestProject.ArchUnitPackage, Packages(vstest));
        Assert.All(vstest.Descendants("PackageReference"), reference => Assert.NotNull(reference.Attribute("Version")));
        Assert.Equal([ArchitectureTestProject.ArchUnitPackage, "xunit.v3.mtp-v2"], Packages(platform));
        Assert.All(platform.Descendants("PackageReference"), reference => Assert.Null(reference.Attribute("Version")));
    }

    [Fact]
    public void ACentralPackageListGetsTheVersionsItLacks()
    {
        const string packages = "<Project>\n  <ItemGroup>\n    <PackageVersion Include=\"xunit.v3.mtp-off\" Version=\"4.0.0\" />\n  </ItemGroup>\n</Project>\n";

        XDocument proposed = XDocument.Parse(ArchitectureTestProject.CentralPackages(root.FullName, packages)!);

        string[] versions = [.. proposed.Descendants("PackageVersion").Select(version => version.Attribute("Include")!.Value)];
        Assert.Equal(1, versions.Count(name => name == "xunit.v3.mtp-off"));
        Assert.Contains(ArchitectureTestProject.ArchUnitPackage, versions);
        Assert.Null(ArchitectureTestProject.CentralPackages(root.FullName, proposed.ToString() + "\n"));
    }

    public void Dispose() => root.Delete(recursive: true);

    private static string[] Packages(XDocument project) => [.. project.Descendants("PackageReference").Select(reference => reference.Attribute("Include")!.Value)];

    private void Project(string relativePath, string properties = "")
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>{properties}</PropertyGroup></Project>");
    }
}
