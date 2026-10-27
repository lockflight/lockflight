using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The full mode's generated .NET project in a scratch area: a production library and an xUnit v3 test project on
/// Microsoft.Testing.Platform, committed to a fresh git repository, plus the spec and tasks a red needs. Building it needs the .NET
/// SDK and the xUnit packages (from NuGet or the local cache).
/// </summary>
internal sealed class CanaryProject(CanaryScratch scratch)
{
    public const string TestsFile = "tests/Canary.Tests/CanaryTests.cs";

    public const string ProductionFile = "src/Canary/Order.cs";

    public const string FeatureDirectory = "specs/20260101-000000-canary";

    private const string GitName = "Lockflight canary";

    private const string GitEmail = "canary@example.com";

    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromSeconds(60);

    /// <summary>Writes the project and commits it; null when that worked, else what failed.</summary>
    public async Task<string?> CreateAsync(CancellationToken cancellationToken)
    {
        Write("global.json", """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" }, "test": { "runner": "Microsoft.Testing.Platform" } }""" + "\n");
        Write(".gitignore", "bin/\nobj/\n.lockflight/ledger/\n.lockflight/local/\n");
        Write("Canary.slnx", """
            <Solution>
              <Project Path="src/Canary/Canary.csproj" />
              <Project Path="tests/Canary.Tests/Canary.Tests.csproj" />
            </Solution>

            """);
        Write("src/Canary/Canary.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>

            """);
        Write(ProductionFile, Order(string.Empty));
        Write("tests/Canary.Tests/Canary.Tests.csproj", """
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
                <ProjectReference Include="../../src/Canary/Canary.csproj" />
              </ItemGroup>
              <ItemGroup>
                <Using Include="Xunit" />
              </ItemGroup>
            </Project>

            """);
        Write(TestsFile, Tests("""
                [Fact]
                public void AnEmptyOrderHasNoLines() => Assert.Equal(0, new Order().LineCount);
            """));
        return await GitAsync(["init", "-q"], cancellationToken) ?? await CommitAsync("canary project", cancellationToken);
    }

    /// <summary>Adds the feature's spec and tasks, so a person can approve them and a red/green cycle can start.</summary>
    public async Task<string?> AddFeatureAsync(CancellationToken cancellationToken)
    {
        Write($"{FeatureDirectory}/spec.md", """
            # Feature Specification: Archive

            ## User Scenarios & Testing *(mandatory)*

            ### User Story 1 - Archive an order (Priority: P1)

            1. **Given** an order, **When** it is archived, **Then** it is marked archived

            ## Requirements *(mandatory)*

            - **FR-001**: An order MUST be archivable

            """);
        Write($"{FeatureDirectory}/tasks.md", """
            # Tasks: Archive

            ## Phase 3: User Story 1 - Archive an order (Priority: P1)

            - [ ] T001 [US1] (FR-001) Declare Order.Archive in src/Canary/Order.cs

            ### Tests for User Story 1 (MANDATORY — write first, ensure they FAIL) ⚠️

            - [ ] T002 [US1] (FR-001) Archive tests in tests/Canary.Tests/CanaryTests.cs

            ### Implementation for User Story 1

            - [ ] T003 [US1] (FR-001) Implement Archive

            """);
        return await CommitAsync("canary feature", cancellationToken);
    }

    /// <summary>Replaces the test class's members.</summary>
    public void WriteTests(string members) => Write(TestsFile, Tests(members));

    /// <summary>Adds members to the production class (stubs a red needs).</summary>
    public void WriteProduction(string members) => Write(ProductionFile, Order(members));

    private static string Order(string members) => $$"""
        namespace Canary;

        public sealed class Order
        {
            public int LineCount => 0;
        {{members}}}

        """;

    private static string Tests(string members) => $$"""
        using Canary;

        namespace Canary.Tests;

        public sealed class CanaryTests
        {
        {{members}}
        }

        """;

    private async Task<string?> CommitAsync(string message, CancellationToken cancellationToken) =>
        await GitAsync(["add", "-A"], cancellationToken)
        ?? await GitAsync(["-c", $"user.name={GitName}", "-c", $"user.email={GitEmail}", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message], cancellationToken);

    private async Task<string?> GitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ProcessResult git = await ProcessRunner.RunAsync("git", arguments, scratch.Project, GitTimeLimit, cancellationToken);
        return git.ExitCode == 0 ? null : $"git {arguments[0]} failed (exit {git.ExitCode}): {git.Combined.Trim()}";
    }

    private void Write(string relativePath, string content)
    {
        string path = scratch.PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
