using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class LockflightPresetRegistryTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-preset-");

    [Theory]
    [InlineData("""{ "presets": { "lockflight": { "version": "0.1.0", "enabled": true } } }""", true)]
    [InlineData("""{ "presets": { "lockflight": { "version": "0.1.0" } } }""", true)]
    [InlineData("""{ "presets": { "lockflight": { "version": "0.1.0", "enabled": false } } }""", false)]
    [InlineData("""{ "presets": { "other": { "enabled": true } } }""", false)]
    [InlineData("{ not json", false)]
    public void ThePresetIsInstalledWhenSpecKitsRegistryHasItEnabled(string registry, bool installed)
    {
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify", "presets"));
        File.WriteAllText(Path.Combine(root.FullName, ".specify", "presets", ".registry"), registry);

        Assert.Equal(installed, LockflightPresetRegistry.IsInstalled(root.FullName));
    }

    [Fact]
    public void WithoutARegistryThePresetIsNotInstalled() => Assert.False(LockflightPresetRegistry.IsInstalled(root.FullName));

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
