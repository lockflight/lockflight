using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;

namespace Lockflight.Cli.Tests.Gates.Steps;

// Runs a real `dotnet build` of a one-file project.
public sealed class BuildStepTests : IDisposable
{
    private readonly DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-build-servers-");

    [Fact]
    public async Task TheBuildServersStayOffWhenTheProjectTurnsThemBackOn()
    {
        Write("Directory.Build.props", """
            <Project>
              <PropertyGroup>
                <UseSharedCompilation>true</UseSharedCompilation>
                <UseRazorBuildServer>true</UseRazorBuildServer>
              </PropertyGroup>
            </Project>
            """);
        Write("Probe.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <Target Name="ProbeBuildServers" BeforeTargets="CoreCompile">
                <Error Condition="'$(UseSharedCompilation)' != 'false' or '$(UseRazorBuildServer)' != 'false'"
                       Text="build servers on: UseSharedCompilation=$(UseSharedCompilation) UseRazorBuildServer=$(UseRazorBuildServer)" />
              </Target>
            </Project>
            """);
        Write("Probe.cs", "namespace Probe; public static class Probe { }\n");
        GateRunContext run = new(project.FullName, project.FullName, GateProfile.Stop, GateSettings.Default, null, TimeProvider.System, UnderStopHook: false);

        GateStep step = await BuildStep.BuildAsync(run, TestContext.Current.CancellationToken);

        Assert.True(step.Verdict == Verdict.Pass, step.Detail);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(project);

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(project.FullName, name), content);
}
