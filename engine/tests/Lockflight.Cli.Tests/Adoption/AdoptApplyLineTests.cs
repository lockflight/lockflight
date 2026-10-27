using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class AdoptApplyLineTests : IDisposable
{
    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    [Fact]
    public void WithoutASpecKitUpgradeTheLineNeedsNoSpecKitOption() =>
        Assert.Equal("cd '/work/shop' && .lockflight/bin/lockflight adopt apply",
            AdoptApplyLine.For("/work/shop", ".lockflight/bin/lockflight", upgradesSpecKit: false, presetDirectory: null));

    [Fact]
    public void ASpecKitUpgradeNamesThePresetByItsAbsolutePath() =>
        Assert.Equal("cd '/work/shop' && .lockflight/bin/lockflight adopt apply --speckit-preset '/opt/lockflight/plugins/lockflight/speckit-preset'",
            AdoptApplyLine.For("/work/shop", ".lockflight/bin/lockflight", upgradesSpecKit: true, presetDirectory: "/opt/lockflight/plugins/lockflight/speckit-preset"));

    [Fact]
    public void ASpecKitUpgradeWithoutAKnownPresetLeavesAPlaceholder() =>
        Assert.Contains("adopt apply --speckit-preset <plugin>/speckit-preset",
            AdoptApplyLine.For("/work/shop", ".lockflight/bin/lockflight", upgradesSpecKit: true, presetDirectory: null), StringComparison.Ordinal);

    [Fact]
    public void ThePresetIsFoundInTheCheckoutOfAnInstallFromSource()
    {
        string checkout = Path.Combine(home.FullName, "src", "lockflight");
        string preset = Path.Combine(checkout, "plugins", "lockflight", "speckit-preset");
        Directory.CreateDirectory(preset);
        Directory.CreateDirectory(Path.Combine(home.FullName, ".lockflight"));
        File.WriteAllText(Path.Combine(home.FullName, ".lockflight", "source"), checkout + "\n");

        Assert.Equal(preset, PresetLocator.Find(home.FullName, "/work/shop"));
    }

    [Fact]
    public void ThePresetIsFoundInTheInstalledPlugin()
    {
        string plugin = Path.Combine(home.FullName, ".claude", "plugins", "cache", "lockflight", "lockflight", "0.2.0");
        Directory.CreateDirectory(Path.Combine(plugin, "speckit-preset"));
        File.WriteAllText(Path.Combine(home.FullName, ".claude", "plugins", "installed_plugins.json"),
            $$"""{ "plugins": { "lockflight@lockflight": [ { "scope": "user", "installPath": "{{plugin.Replace("\\", "\\\\", StringComparison.Ordinal)}}" } ] } }""");

        Assert.Equal(Path.Combine(plugin, "speckit-preset"), PresetLocator.Find(home.FullName, "/work/shop"));
    }

    [Fact]
    public void NoInstallMeansNoPreset() => Assert.Null(PresetLocator.Find(home.FullName, "/work/shop"));

    public void Dispose() => home.Delete(recursive: true);
}
