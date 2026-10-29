using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class ProductionProjectsTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-production-");

    [Fact]
    public void ProjectsThatAreNotTestCodeAreProduction()
    {
        Project("src/Shop.Api/Shop.Api.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Project("src/Shop.Core/Shop.Core.csproj", "<Project><PropertyGroup><AssemblyName>Shop.Domain</AssemblyName></PropertyGroup></Project>");
        Project("src/Shop.Generated/Shop.Generated.csproj", "<Project><PropertyGroup><AssemblyName>$(Prefix).Gen</AssemblyName></PropertyGroup></Project>");
        Project("tests/Shop.Tests/Shop.Tests.csproj", "<Project />");
        Project("tests/Shop.Testing/Shop.Testing.csproj", "<Project><ItemGroup><PackageReference Include=\"xunit.v3.assert\" /></ItemGroup></Project>");
        Project("tests/Shop.NUnitHelpers/Shop.NUnitHelpers.csproj", "<Project><ItemGroup><PackageReference Include=\"NUnit\" /></ItemGroup></Project>");
        Project("src/Shop.Api/bin/Debug/Copy.csproj", "<Project />");

        ProductionCode production = ProductionProjects.Find(root.FullName, [App("tests/Shop.Tests", "tests/Shop.Tests/bin/Shop.Tests.dll")]);

        Assert.Equal(["Shop.Api", "Shop.Domain", "Shop.Generated"], production.AssemblyNames.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["src/Shop.Api", "src/Shop.Core", "src/Shop.Generated"],
            production.ProjectDirectories.Select(directory => Path.GetRelativePath(root.FullName, directory).Replace('\\', '/')).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TypesAreReadFromTheProductionAssembliesInTheTestOutput()
    {
        string engine = typeof(RedClass).Assembly.Location;
        string engineName = Path.GetFileNameWithoutExtension(engine);
        Project($"src/{engineName}/{engineName}.csproj", "<Project />");
        Directory.CreateDirectory(Path.Combine(root.FullName, "tests/Shop.Tests/bin"));
        File.Copy(engine, Path.Combine(root.FullName, "tests/Shop.Tests/bin", engineName + ".dll"));

        ProductionCode production = ProductionProjects.Find(root.FullName, [App("tests/Shop.Tests", "tests/Shop.Tests/bin/Shop.Tests.dll")]);

        Assert.Contains(typeof(RedClassifier).FullName!, production.TypeNames);
        Assert.Contains("Lockflight.Cli.Lifecycle.RedGreen.TestCodeScanner", production.TypeNames);
    }

    public void Dispose() => root.Delete(recursive: true);

    private TestApp App(string directory, string assembly) => new(Path.Combine(root.FullName, directory), Path.Combine(root.FullName, assembly));

    private void Project(string path, string content)
    {
        string full = Path.Combine(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
