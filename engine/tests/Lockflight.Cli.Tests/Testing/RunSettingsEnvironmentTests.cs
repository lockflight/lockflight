using Lockflight.Cli.Gates;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class RunSettingsEnvironmentTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-runsettings-");

    [Fact]
    public void TheVariablesOfTheRunConfigurationAreRead()
    {
        string file = Write("""
            <RunSettings>
              <RunConfiguration>
                <EnvironmentVariables>
                  <TZ>Europe/Kyiv</TZ>
                  <ASPNETCORE_ENVIRONMENT>Testing</ASPNETCORE_ENVIRONMENT>
                </EnvironmentVariables>
              </RunConfiguration>
            </RunSettings>
            """);

        IReadOnlyDictionary<string, string?> variables = RunSettingsEnvironment.Read(file);

        Assert.Equal(["ASPNETCORE_ENVIRONMENT=Testing", "TZ=Europe/Kyiv"], variables.Select(variable => $"{variable.Key}={variable.Value}").Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("<RunSettings><RunConfiguration /></RunSettings>")]
    [InlineData("<RunSettings><RunConfiguration>")]
    public void RunSettingsWithoutVariablesOrUnreadableSetNone(string content) => Assert.Empty(RunSettingsEnvironment.Read(Write(content)));

    [Fact]
    public void AMissingFileSetsNone() => Assert.Empty(RunSettingsEnvironment.Read(Path.Combine(root.FullName, "missing.runsettings")));

    [Theory]
    [InlineData((int)TestRunnerKind.VSTest, true)]
    [InlineData((int)TestRunnerKind.MicrosoftTestingPlatform, false)]
    public void TheVariablesApplyWhereDotnetTestAppliesThem(int platform, bool applies) =>
        Assert.Equal(applies, RunSettingsEnvironment.AppliesTo((TestRunnerKind)platform));

    public void Dispose() => root.Delete(recursive: true);

    private string Write(string content)
    {
        string file = Path.Combine(root.FullName, "test.runsettings");
        File.WriteAllText(file, content);
        return file;
    }
}
