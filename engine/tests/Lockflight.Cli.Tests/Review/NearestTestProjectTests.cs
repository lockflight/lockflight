using Lockflight.Cli.Review;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Review;

public sealed class NearestTestProjectTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-nearest-");

    public NearestTestProjectTests()
    {
        Project("src/Billing/Billing.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Project("src/Shop.Core/Shop.Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Project("src/Shop.Api/Shop.Api.csproj", References("../Shop.Core/Shop.Core.csproj"));
        Project("tests/Billing.Tests/Billing.Tests.csproj", References("..\\..\\src\\Billing\\Billing.csproj"));
        Project("tests/Shop.Api.Tests/Shop.Api.Tests.csproj", References("../../src/Shop.Api/Shop.Api.csproj"));
    }

    [Fact]
    public void TheTestProjectReferencingTheCitedFilesProjectIsChosenOverTheFirst()
    {
        TestApp chosen = NearestTestProject.Choose(Apps(), root.FullName, "src/Shop.Api/Orders/OrderEndpoint.cs");

        Assert.Equal("Shop.Api.Tests", chosen.AssemblyName);
    }

    [Fact]
    public void AProjectReachedThroughAnotherProjectCounts()
    {
        TestApp chosen = NearestTestProject.Choose(Apps(), root.FullName, "src/Shop.Core/Pricing.cs");

        Assert.Equal("Shop.Api.Tests", chosen.AssemblyName);
    }

    [Fact]
    public void ADirectReferenceBeatsOneThroughAnotherProject()
    {
        Project("tests/Shop.Core.Tests/Shop.Core.Tests.csproj", References("../../src/Shop.Core/Shop.Core.csproj"));

        TestApp chosen = NearestTestProject.Choose([.. Apps(), App("tests/Shop.Core.Tests")], root.FullName, "src/Shop.Core/Pricing.cs");

        Assert.Equal("Shop.Core.Tests", chosen.AssemblyName);
    }

    [Fact]
    public void AFileInsideATestProjectIsBuiltThere()
    {
        TestApp chosen = NearestTestProject.Choose(Apps(), root.FullName, "tests/Shop.Api.Tests/Fakes/FakeClock.cs");

        Assert.Equal("Shop.Api.Tests", chosen.AssemblyName);
    }

    [Fact]
    public void AFileNoTestProjectReachesFallsBackToTheFirst()
    {
        TestApp chosen = NearestTestProject.Choose(Apps(), root.FullName, "scripts/tool.cs");

        Assert.Equal("Billing.Tests", chosen.AssemblyName);
    }

    public void Dispose() => root.Delete(recursive: true);

    private static string References(string path) => $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"{path}\" /></ItemGroup></Project>";

    private TestApp[] Apps() => [App("tests/Billing.Tests"), App("tests/Shop.Api.Tests")];

    private TestApp App(string directory)
    {
        string full = Path.Combine(root.FullName, directory);
        return new TestApp(full, Path.Combine(full, "bin", "Debug", "net10.0", Path.GetFileName(directory) + ".dll"));
    }

    private void Project(string path, string content)
    {
        string full = Path.Combine(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
