namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A small .NET solution in a scripted history for the judge's real-build tests: a production library <c>Shop</c> and an xUnit v3
/// test project on Microsoft.Testing.Platform. Building it needs the .NET SDK and the xUnit packages (from NuGet or the cache).
/// </summary>
internal static class CiSolution
{
    public const string ProductionFile = "src/Shop/Price.cs";
    public const string TestsFile = "tests/Shop.Tests/PriceTests.cs";

    /// <summary>Writes the solution with the given production members and test members.</summary>
    public static GitHistoryFixture WriteSolution(this GitHistoryFixture repository, string production, string tests)
    {
        repository.Write("global.json", """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" }, "test": { "runner": "Microsoft.Testing.Platform" } }""" + "\n")
            .Write(".gitignore", "bin/\nobj/\n.lockflight/local/\n.lockflight/ledger/\n")
            .Write("lockflight.yaml", "version: 1\n")
            .Write("Shop.slnx", """
                <Solution>
                  <Project Path="src/Shop/Shop.csproj" />
                  <Project Path="tests/Shop.Tests/Shop.Tests.csproj" />
                </Solution>

                """)
            .Write("src/Shop/Shop.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                </Project>

                """)
            .Write("tests/Shop.Tests/Shop.Tests.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <OutputType>Exe</OutputType>
                    <IsPackable>false</IsPackable>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="../../src/Shop/Shop.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <Using Include="Xunit" />
                  </ItemGroup>
                </Project>

                """);
        return repository.WriteCode(production, tests);
    }

    /// <summary>Replaces the production class's and the test class's members; the test class derives from <paramref name="testBase"/> when given.</summary>
    public static GitHistoryFixture WriteCode(this GitHistoryFixture repository, string production, string tests, string? testBase = null) => repository
        .Write(ProductionFile, $$"""
            namespace Shop;

            public sealed class Price
            {
            {{production}}
            }

            """)
        .Write(TestsFile, $$"""
            using Shop;

            namespace Shop.Tests;

            public sealed class PriceTests{{(testBase is null ? string.Empty : $" : {testBase}")}}
            {
                [Fact]
                public void ABaseTestPasses() => Assert.True(true);

            {{tests}}
            }

            """);
}
