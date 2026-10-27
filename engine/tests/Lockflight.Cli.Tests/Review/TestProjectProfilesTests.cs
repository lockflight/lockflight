using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class TestProjectProfilesTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-profiles-");

    [Fact]
    public void AProfileNamesTheFrameworksTheTestFrameworkAndTheReferences()
    {
        Write("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Write("tests/Shop.Tests/Shop.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />
                <PackageReference Include="NSubstitute" Version="5.3.0" />
                <ProjectReference Include="../../src/Shop/Shop.csproj" />
              </ItemGroup>
            </Project>
            """);

        TestProjectProfile profile = TestProjectProfiles.Describe(Path.Combine(root.FullName, "tests/Shop.Tests/Shop.Tests.csproj"), root.FullName);

        Assert.Equal("Shop.Tests", profile.Name);
        Assert.Equal("tests/Shop.Tests/Shop.Tests.csproj", profile.Path);
        Assert.Equal("net10.0", profile.TargetFrameworks);
        Assert.Equal("xUnit v3", profile.TestFramework);
        Assert.Equal(["src/Shop/Shop.csproj"], profile.ProjectReferences);
        Assert.Equal(["xunit.v3.mtp-v2", "NSubstitute"], profile.PackageReferences);
    }

    [Fact]
    public void OnlyXunitProjectsAreFound()
    {
        Write("src/Shop/Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Write("tests/Shop.Testing/Shop.Testing.csproj", "<Project><ItemGroup><PackageReference Include=\"xunit.v3.assert\" /></ItemGroup></Project>");
        Write("tests/Shop.Tests/Shop.Tests.csproj", "<Project><ItemGroup><PackageReference Include=\"xunit\" Version=\"2.9.3\" /></ItemGroup></Project>");
        Write("tests/Shop.Tests/bin/Debug/Copy.csproj", "<Project><ItemGroup><PackageReference Include=\"xunit\" /></ItemGroup></Project>");

        IReadOnlyList<string> projects = TestProjectProfiles.Find(root.FullName);

        Assert.Equal(["tests/Shop.Tests/Shop.Tests.csproj"], projects.Select(project => TestProjectProfiles.Relative(root.FullName, project)));
        Assert.Equal("xUnit v2 (VSTest)", TestProjectProfiles.Describe(projects[0], root.FullName).TestFramework);
    }

    [Fact]
    public void UsingsComeFromTheTopOfEachFileAndAnAliasKeepsOneMeaning()
    {
        Write("tests/Shop.Tests/OrderTests.cs", "using Shop.Orders;\nusing Clock = Shop.Time.Clock;\n\nnamespace Shop.Tests\n{\n    using Fakes;\n}\n");
        Write("tests/Shop.Tests/PriceTests.cs", "using System.Text;\nusing Clock = System.TimeProvider;\n\nnamespace Shop.Tests;\n");
        Write("tests/Shop.Tests/obj/Debug/Generated.cs", "using Hidden.Namespace;\n");

        IReadOnlyList<string> usings = TestProjectProfiles.Usings(Path.Combine(root.FullName, "tests/Shop.Tests"));

        Assert.Equal(["using Clock = Shop.Time.Clock;", "using Shop.Orders;", "using System.Text;", "using Xunit;"], usings);
    }

    [Fact]
    public void ASampleTestShowsTheFirstFactWithItsUsingsAndClass()
    {
        Write("tests/Shop.Tests/A/Helpers.cs", "namespace Shop.Tests;\n\ninternal static class Helpers { }\n");
        Write("tests/Shop.Tests/OrderTests.cs", """
            using Shop.Orders;

            namespace Shop.Tests;

            public sealed class OrderTests
            {
                [Fact]
                public void TotalAppliesTheDiscount()
                {
                    Assert.Equal(90m, Pricing.Total(100m, 10m));
                }

                [Fact]
                public void Other() => Assert.True(true);
            }
            """);

        string? sample = TestProjectProfiles.SampleTest(Path.Combine(root.FullName, "tests/Shop.Tests"), root.FullName);

        Assert.Equal(
            """
            // tests/Shop.Tests/OrderTests.cs
            using Shop.Orders;
            namespace Shop.Tests;
            public sealed class OrderTests
            [Fact]
                public void TotalAppliesTheDiscount()
                {
                    Assert.Equal(90m, Pricing.Total(100m, 10m));
                }

            """.ReplaceLineEndings("\n"),
            sample);
    }

    public void Dispose() => root.Delete(recursive: true);

    private void Write(string path, string content)
    {
        string full = Path.Combine(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
