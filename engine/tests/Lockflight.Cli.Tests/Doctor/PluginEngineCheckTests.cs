using System.Text.Json;
using Lockflight.Cli.Doctor;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class PluginEngineCheckTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-plugin-engine-");

    private string Home => Path.Combine(root.FullName, "home");

    private string Project => Path.Combine(root.FullName, "project");

    [Fact]
    public void APinOlderThanThePluginNeedsFailsWithTheUpdateAsAPersonsStep()
    {
        PinEngine("0.2.0-alpha.3");
        InstallPlugin(("user", null, "0.2.0-alpha.4"));

        DoctorCheck check = PluginEngineCheck.Check(Project, Home)!;

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("needs engine 0.2.0-alpha.4 or newer; the project pins 0.2.0-alpha.3", check.Detail, StringComparison.Ordinal);
        Assert.Contains("~/.lockflight/bin/lockflight update", check.Fix, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0-alpha.4")]
    [InlineData("0.2.0-alpha.4.dev.20261015101143")]
    [InlineData("0.2.0")]
    public void APinAtOrAboveThePluginsMinimumPasses(string pinned)
    {
        PinEngine(pinned);
        InstallPlugin(("user", null, "0.2.0-alpha.4"));

        Assert.Equal(CheckStatus.Pass, PluginEngineCheck.Check(Project, Home)!.Status);
    }

    [Fact]
    public void TheProjectsOwnPluginInstallWinsOverTheUserScopeOne()
    {
        PinEngine("0.2.0-alpha.4");
        InstallPlugin(("user", null, "0.2.0-alpha.3"), ("project", Project, "0.2.0-alpha.5"));

        Assert.Equal(CheckStatus.Fail, PluginEngineCheck.Check(Project, Home)!.Status);
    }

    [Fact]
    public void WithoutAPluginRecordOrRequirementThereIsNothingToCheck()
    {
        PinEngine("0.2.0-alpha.4");
        Assert.Null(PluginEngineCheck.Check(Project, Home));

        InstallPlugin(("user", null, null));
        Assert.Null(PluginEngineCheck.Check(Project, Home));

        File.WriteAllText(Path.Combine(Home, ".claude", "plugins", "installed_plugins.json"), "{ not json");
        Assert.Null(PluginEngineCheck.Check(Project, Home));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private void PinEngine(string version)
    {
        Directory.CreateDirectory(Project);
        File.WriteAllText(Path.Combine(Project, "dotnet-tools.json"), $$"""{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "{{version}}", "commands": ["lockflight"] } } }""");
    }

    private void InstallPlugin(params (string Scope, string? ProjectPath, string? MinimumEngine)[] installs)
    {
        List<Dictionary<string, string>> records = [];
        foreach ((string scope, string? projectPath, string? minimumEngine) in installs)
        {
            string installPath = Path.Combine(Home, ".claude", "plugins", "cache", "lockflight", "lockflight", $"install-{records.Count}");
            Directory.CreateDirectory(installPath);
            if (minimumEngine is not null)
            {
                File.WriteAllText(Path.Combine(installPath, "min-engine-version"), minimumEngine + "\n");
            }

            Dictionary<string, string> record = new() { ["scope"] = scope, ["installPath"] = installPath };
            if (projectPath is not null)
            {
                record["projectPath"] = projectPath;
            }

            records.Add(record);
        }

        string file = Path.Combine(Home, ".claude", "plugins", "installed_plugins.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = 2,
            ["plugins"] = new Dictionary<string, object> { ["lockflight@lockflight"] = records },
        }));
    }
}
