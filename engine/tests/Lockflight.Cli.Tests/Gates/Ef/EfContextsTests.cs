using Lockflight.Cli.Gates.Ef;

namespace Lockflight.Cli.Tests.Gates.Ef;

public sealed class EfContextsTests : IDisposable
{
    private const string DesignReference = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.EntityFrameworkCore.Design\" Version=\"10.0.0\" /></ItemGroup></Project>\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-ef-contexts-");

    [Fact]
    public void AProjectThatReferencesTheDesignPackageIsItsOwnStartupProject()
    {
        Write("Shop/Shop.csproj", DesignReference);
        Write("Shop/Migrations/ShopContextModelSnapshot.cs", "[DbContext(typeof(ShopContext))]\n");

        EfContext context = Assert.Single(EfContexts.Find(root.FullName, root.FullName));

        Assert.Equal(("Shop/Shop.csproj", "Shop", "ShopContext", (string?)null), (context.Project, context.Directory, context.Context, context.StartupProject));
        Assert.Equal(["Shop"], context.ModelDirectories);
    }

    [Fact]
    public void TwoContextsOfOneProjectAreBothFoundAndBuildOutputIsNotSearched()
    {
        Write("Shop/Shop.csproj", DesignReference);
        Write("Shop/Migrations/ShopContextModelSnapshot.cs", "[DbContext(typeof(ShopContext))]\n");
        Write("Shop/Migrations/Audit/AuditContextModelSnapshot.cs", "[DbContext(typeof(Shop.Audit.AuditContext))]\n");
        Write("Shop/bin/Debug/Migrations/OldContextModelSnapshot.cs", "[DbContext(typeof(OldContext))]\n");

        IReadOnlyList<EfContext> contexts = EfContexts.Find(root.FullName, root.FullName);

        Assert.Equal(["AuditContext", "ShopContext"], contexts.Select(context => context.Context).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASnapshotOutsideAMigrationsFolderOrWithoutAContextIsIgnored()
    {
        Write("Shop/Shop.csproj", DesignReference);
        Write("Shop/Model/ShopContextModelSnapshot.cs", "[DbContext(typeof(ShopContext))]\n");
        Write("Shop/Migrations/BrokenModelSnapshot.cs", "class Broken { }\n");

        Assert.Empty(EfContexts.Find(root.FullName, root.FullName));
    }

    [Fact]
    public void AToolManifestWithoutDotnetEfOrUnreadablePinsNothing()
    {
        Write("dotnet-tools.json", """{ "version": 1, "tools": { "lockflight.cli": { "version": "1.0.0" } } }""");
        Write("app/.config/dotnet-tools.json", "{ not json");

        Assert.Null(EfToolPin.Find(root.FullName, Path.Combine(root.FullName, "app")));
    }

    [Fact]
    public void APinAboveTheUnitUpToTheProjectRootCounts()
    {
        Write("dotnet-tools.json", """{ "version": 1, "tools": { "Dotnet-EF": { "version": "10.0.0" } } }""");
        Directory.CreateDirectory(Path.Combine(root.FullName, "services", "orders"));

        Assert.Equal("dotnet-tools.json", EfToolPin.Find(root.FullName, Path.Combine(root.FullName, "services", "orders")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
