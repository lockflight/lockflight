using System.Text.Json;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Adoption;

/// <summary>
/// The Spec Kit preset init names is the one that matches this engine: the installed plugin's when its version is the engine's,
/// else the checkout a source install was built from, and an older plugin's only when nothing else is there.
/// </summary>
public sealed class PresetLocatorTests : IDisposable
{
    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-preset-home-");
    private readonly DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-preset-project-");

    [Fact]
    public void ACheckoutPresetWinsOverAnOlderPlugin()
    {
        InstallPlugin("0.0.1-old");
        string checkout = Checkout();

        Assert.Equal(Path.Combine(checkout, "plugins", "lockflight", "speckit-preset"), PresetLocator.Find(home.FullName, project.FullName));
    }

    [Fact]
    public void ThePluginOfTheEnginesVersionWins()
    {
        string plugin = InstallPlugin(ToolPin.RunningVersion);
        Checkout();

        Assert.Equal(Path.Combine(plugin, "speckit-preset"), PresetLocator.Find(home.FullName, project.FullName));
    }

    [Fact]
    public void AnOlderPluginIsNamedWhenNothingElseIsThere()
    {
        string plugin = InstallPlugin("0.0.1-old");

        Assert.Equal(Path.Combine(plugin, "speckit-preset"), PresetLocator.Find(home.FullName, project.FullName));
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(home);
        TempDirectories.DeleteRecursively(project);
    }

    private string InstallPlugin(string version)
    {
        string plugin = Path.Combine(home.FullName, ".claude", "plugins", "cache", "lockflight", "lockflight", version);
        Directory.CreateDirectory(Path.Combine(plugin, "speckit-preset"));
        Directory.CreateDirectory(Path.Combine(plugin, ".claude-plugin"));
        File.WriteAllText(Path.Combine(plugin, ".claude-plugin", "plugin.json"), JsonSerializer.Serialize(new Dictionary<string, string> { ["version"] = version }));
        Dictionary<string, string> record = new() { ["scope"] = "user", ["installPath"] = plugin };
        File.WriteAllText(Path.Combine(home.FullName, ".claude", "plugins", "installed_plugins.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["version"] = 2,
            ["plugins"] = new Dictionary<string, object> { ["lockflight@lockflight"] = new[] { record } },
        }));
        return plugin;
    }

    private string Checkout()
    {
        string checkout = Path.Combine(home.FullName, "src", "lockflight");
        Directory.CreateDirectory(Path.Combine(checkout, "plugins", "lockflight", "speckit-preset"));
        Directory.CreateDirectory(Path.Combine(home.FullName, ".lockflight"));
        File.WriteAllText(Path.Combine(home.FullName, ".lockflight", "source"), checkout + "\n");
        return checkout;
    }
}
