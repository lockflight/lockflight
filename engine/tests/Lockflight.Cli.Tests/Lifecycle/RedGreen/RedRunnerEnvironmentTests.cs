using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Canaries;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

/// <summary>
/// A real red on a generated project (<see cref="CanaryProject"/>), compared with a plain <c>dotnet test</c>: the red runs each test
/// application in its output folder with its run settings' variables, as <c>dotnet test</c> does, prints a <c>dotnet test</c> line per
/// app, and re-runs a GREEN_BEFORE test through <c>dotnet test</c>. Builds a project, so tens of seconds each.
/// </summary>
public sealed class RedRunnerEnvironmentTests : IDisposable
{
    private const string Passing = """
            [Fact]
            public void AnEmptyOrderHasNoLines() => Assert.Equal(0, new Order().LineCount);
        """;

    private const string Stub = """

            public void Archive() => throw new NotImplementedException();

        """;

    private readonly CanaryScratch scratch = new(TimeProvider.System);

    [Fact]
    public async Task ARedRunsTheTestsInTheirOutputFolderWithTheVariablesOfTheRunSettingsAsDotnetTestDoes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CanaryProject project = await StartAsync(onVSTest: true, cancellationToken);
        project.WriteProduction(Stub);
        project.WriteTests(Passing + """

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesWithItsFixture()
                {
                    if (!File.Exists("fixture.txt"))
                    {
                        throw new InvalidOperationException("fixture.txt is not in the working directory");
                    }

                    new Order().Archive();
                }

                [Fact]
                [Trait("Requirement", "FR-001")]
                public void ArchivesWithTheRunSettingsVariable()
                {
                    if (Environment.GetEnvironmentVariable("CANARY_MODE") != "from-runsettings")
                    {
                        throw new InvalidOperationException("CANARY_MODE is not set");
                    }

                    new Order().Archive();
                }
            """);

        CanaryRun red = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);

        Assert.True(red.ExitCode == ExitCode.Pass, red.Output + red.Error);
        Assert.Contains("RED_STUB     Canary.Tests.CanaryTests.ArchivesWithItsFixture", red.Output, StringComparison.Ordinal);
        Assert.Contains("RED_STUB     Canary.Tests.CanaryTests.ArchivesWithTheRunSettingsVariable", red.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Reproduce Canary.Tests with dotnet test: cd " + ShellQuoting.QuoteIfNeeded(scratch.PathOf("tests/Canary.Tests"))
            + " && dotnet test Canary.Tests.csproj --filter 'FullyQualifiedName=Canary.Tests.CanaryTests.ArchivesWithItsFixture|FullyQualifiedName=Canary.Tests.CanaryTests.ArchivesWithTheRunSettingsVariable'",
            red.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AGreenBeforeTestThatFailsUnderDotnetTestKeepsItsVerdictAndSaysWhy(bool onVSTest)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CanaryProject project = await StartAsync(onVSTest, cancellationToken);
        project.WriteProduction(Stub);
        project.WriteTests(Passing + """

                // Passes when the app runs on its own; dotnet test starts it as a server (Microsoft.Testing.Platform) or with a
                // response file of VSTest's (@@).
                [Fact]
                [Trait("Requirement", "FR-001")]
                public void PassesOutsideDotnetTest()
                {
                    if (Environment.GetCommandLineArgs().Any(argument => argument is "--server" or "@@"))
                    {
                        throw new InvalidOperationException("started by dotnet test");
                    }

                    Assert.Equal(0, new Order().LineCount);
                }
            """);

        CanaryRun red = await scratch.RunAsAgentAsync(["redgreen", "red"], string.Empty, cancellationToken);

        Assert.True(red.ExitCode == ExitCode.Fail, red.Output + red.Error);
        Assert.Contains("GREEN_BEFORE Canary.Tests.CanaryTests.PassesOutsideDotnetTest", red.Output, StringComparison.Ordinal);
        Assert.Contains(
            "Warning: Canary.Tests.CanaryTests.PassesOutsideDotnetTest passes under red's run but fails under dotnet test — the difference is the runner environment",
            red.Output,
            StringComparison.Ordinal);
        Assert.Contains("dotnet test: cd ", red.Output, StringComparison.Ordinal);
        Assert.Contains("red's run:   cd ", red.Output, StringComparison.Ordinal);
        Assert.Contains(
            onVSTest
                ? " && dotnet test Canary.Tests.csproj --no-build --filter FullyQualifiedName=Canary.Tests.CanaryTests.PassesOutsideDotnetTest"
                : " && dotnet test --project Canary.Tests.csproj --no-build --filter-method Canary.Tests.CanaryTests.PassesOutsideDotnetTest",
            red.Output,
            StringComparison.Ordinal);
        Assert.Contains("Red rejected", red.Output, StringComparison.Ordinal);
    }

    public void Dispose() => scratch.Dispose();

    /// <summary>
    /// The canary project with its feature approved and a red/green cycle started; <paramref name="onVSTest"/> moves it to VSTest
    /// (xUnit v3 without the testing platform, a run-settings file named by <c>RunSettingsFilePath</c>, a fixture file copied to the
    /// output folder).
    /// </summary>
    private async Task<CanaryProject> StartAsync(bool onVSTest, CancellationToken cancellationToken)
    {
        CanaryProject project = new(scratch);
        Assert.Null(await project.CreateAsync(cancellationToken));
        if (onVSTest)
        {
            File.WriteAllText(scratch.PathOf("global.json"), """{ "sdk": { "version": "10.0.100", "rollForward": "latestMajor" } }""" + "\n");
            File.WriteAllText(scratch.PathOf("tests/Canary.Tests/Canary.Tests.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <OutputType>Exe</OutputType>
                    <IsPackable>false</IsPackable>
                    <RunSettingsFilePath>$(MSBuildProjectDirectory)/../test.runsettings</RunSettingsFilePath>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="xunit.v3.mtp-off" Version="4.0.2" />
                    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.1" />
                    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
                  </ItemGroup>
                  <ItemGroup>
                    <ProjectReference Include="../../src/Canary/Canary.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <Using Include="Xunit" />
                    <None Include="fixture.txt" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>

                """);
            File.WriteAllText(scratch.PathOf("tests/Canary.Tests/fixture.txt"), "fixture\n");
            File.WriteAllText(scratch.PathOf("tests/test.runsettings"), """
                <RunSettings>
                  <RunConfiguration>
                    <EnvironmentVariables>
                      <CANARY_MODE>from-runsettings</CANARY_MODE>
                    </EnvironmentVariables>
                  </RunConfiguration>
                </RunSettings>

                """);
        }

        Assert.Null(await project.AddFeatureAsync(cancellationToken));
        foreach (string[] approval in new[] { new[] { "approve", "spec" }, ["approve", "tasks"] })
        {
            CanaryRun approved = await scratch.RunAsPersonAsync(approval, cancellationToken);
            Assert.True(approved.ExitCode == ExitCode.Pass, approved.Output + approved.Error);
        }

        CanaryRun start = await scratch.RunAsAgentAsync(["redgreen", "start"], string.Empty, cancellationToken);
        Assert.True(start.ExitCode == ExitCode.Pass, start.Output + start.Error);
        return project;
    }
}
