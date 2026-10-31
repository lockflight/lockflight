using Lockflight.Cli.Ci;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class TestHelperProjectsTests
{
    private const string Library = "<Project Sdk=\"Microsoft.NET.Sdk\" />";

    [Fact]
    public void OneDefinitionCoversFrameworkMarkersAndTestOnlySupportLibraries()
    {
        Dictionary<string, string> projects = new()
        {
            ["src/Shop/Shop.csproj"] = Library,
            ["src/Shop.Api/Shop.Api.csproj"] = References("../Shop/Shop.csproj", "../../tests/Shop.Shared/Shop.Shared.csproj"),
            ["tests/Shop.Tests/Shop.Tests.csproj"] = References("../../src/Shop/Shop.csproj", "../Shop.Support/Shop.Support.csproj", "../Shop.NUnitHelpers/Shop.NUnitHelpers.csproj", "../Shop.Shared/Shop.Shared.csproj", "../../src/Shop.Asserts/Shop.Asserts.csproj"),
            ["tests/Shop.Support/Shop.Support.csproj"] = References("../Shop.Fakes/Shop.Fakes.csproj"),
            ["tests/Shop.Fakes/Shop.Fakes.csproj"] = Library,
            ["tests/Shop.Tests2/Shop.Tests2.csproj"] = References("../../src/Shop.Testimonials/Shop.Testimonials.csproj", "../../Root.Testing.csproj"),
            ["tests/Shop.NUnitHelpers/Shop.NUnitHelpers.csproj"] = "<Project><ItemGroup><PackageReference Include=\"NUnit\" /></ItemGroup></Project>",
            ["tests/Shop.Shared/Shop.Shared.csproj"] = Library,
            ["src/Shop.Asserts/Shop.Asserts.csproj"] = "<Project><ItemGroup><PackageReference Include=\"Shouldly\" /></ItemGroup></Project>",
            ["tests/Orphan/Orphan.csproj"] = Library,
            ["src/Shop.Testimonials/Shop.Testimonials.csproj"] = Library,
            ["Root.Testing.csproj"] = References("src/Shop/Shop.csproj"),
        };

        IReadOnlySet<string> helpers = TestHelperProjects.Find(projects, new HashSet<string>(["tests/Shop.Tests/Shop.Tests.csproj", "tests/Shop.Tests2/Shop.Tests2.csproj"]));

        Assert.Equal(
            ["Root.Testing.csproj", "src/Shop.Asserts/Shop.Asserts.csproj", "tests/Shop.Fakes/Shop.Fakes.csproj", "tests/Shop.NUnitHelpers/Shop.NUnitHelpers.csproj", "tests/Shop.Support/Shop.Support.csproj"],
            helpers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ATestProjectFileIsAnInputOfItsTests()
    {
        TestSide side = new(new Dictionary<string, string> { ["Shop.Tests"] = "tests/Shop.Tests" }, [], []);

        Assert.True(side.AffectsBeyondCode("tests/Shop.Tests/Shop.Tests.csproj", "Shop.Tests"));
        Assert.False(side.AffectsBeyondCode("tests/Shop.Tests/obj/project.assets.json", "Shop.Tests"));
    }

    private static string References(params string[] paths) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>{string.Concat(paths.Select(path => $"<ProjectReference Include=\"{path}\" />"))}</ItemGroup></Project>";
}
