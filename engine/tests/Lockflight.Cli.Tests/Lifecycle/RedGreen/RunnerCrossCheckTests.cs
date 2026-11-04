using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class RunnerCrossCheckTests
{
    [Fact]
    public void TheWarningGivesBothCommandsTheirWorkingDirectoriesAndEnvironments()
    {
        string project = Path.Combine(Path.GetTempPath(), "shop", "tests", "Shop.Tests");
        TestApp app = new(project, Path.Combine(project, "bin", "Debug", "net10.0", "Shop.Tests.dll"))
        {
            ProjectFile = Path.Combine(project, "Shop.Tests.csproj"),
            RunSettingsFile = Path.Combine(project, "test.runsettings"),
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal) { ["TZ"] = "Europe/Kyiv" },
        };
        TestCommand dotnetTest = TestCommands.DotnetTest(app, ["Shop.Tests.ZoneTests.Converts"], noBuild: true)!;
        Dictionary<string, string?> inherited = new(StringComparer.Ordinal) { ["LANG"] = "C", ["ASPNETCORE_ConnectionStrings__Shop"] = "Server=db;Password=hunter2" };

        string[] lines = RunnerCrossCheck.Warning(app, "Shop.Tests.ZoneTests.Converts", ["Shop.Tests.ZoneTests.Converts", "Shop.Tests.ZoneTests.Parses"], dotnetTest, inherited).Split(Environment.NewLine);

        Assert.StartsWith("Warning: Shop.Tests.ZoneTests.Converts passes under red's run but fails under dotnet test — the difference is the runner environment (", lines[0], StringComparison.Ordinal);
        Assert.Contains("The verdict stays GREEN_BEFORE", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.Equal($"  red's run:   cd {ShellQuoting.QuoteIfNeeded(app.OutputDirectory)} && dotnet {ShellQuoting.QuoteIfNeeded(app.AssemblyPath)} -noLogo -noColor -method Shop.Tests.ZoneTests.Converts -method Shop.Tests.ZoneTests.Parses", lines[1]);
        Assert.Equal("               environment: ASPNETCORE_ConnectionStrings__Shop=<redacted> DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 LANG=C TZ=Europe/Kyiv", lines[2]);
        Assert.Equal($"  dotnet test: cd {ShellQuoting.QuoteIfNeeded(project)} && dotnet test Shop.Tests.csproj --no-build --filter FullyQualifiedName=Shop.Tests.ZoneTests.Converts", lines[3]);
        Assert.Equal($"               environment: ASPNETCORE_ConnectionStrings__Shop=<redacted> DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 LANG=C (and the variables of {app.RunSettingsFile}, which dotnet test applies itself)", lines[4]);
    }
}
