using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class WorkspaceChecksTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-wsdoctor-");

    private readonly DirectoryInfo home = Directory.CreateTempSubdirectory("lockflight-home-");

    [Fact]
    public async Task AWorkspaceIsCheckedUnitByUnit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Write(Manifest.FileName, "version: 1\nunits:\n  - path: services/orders\n  - path: services/billing\n  - path: services/missing\n");
        Write("services/billing/lockflight.yaml", "version: 1\n");
        Write("services/orders/global.json", """{ "sdk": { "version": "8.0.416", "rollForward": "latestFeature" } }""");
        await GitAsync(root.FullName, ["init", "-q"]);
        await GitAsync(Path.Combine(root.FullName, "services", "orders"), ["init", "-q"]);

        Dictionary<string, DoctorCheck> checks = (await WorkspaceChecks.RunAsync(root.FullName, ManifestLocator.Discover(root.FullName).Manifest, home.FullName, DateTimeOffset.UnixEpoch, cancellationToken))
            .ToDictionary(check => check.Name);

        Assert.Equal(CheckStatus.Warn, checks["units"].Status);
        Assert.Contains("services/missing", checks["units"].Detail, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Fail, checks["unit manifests"].Status);
        Assert.Contains("services/billing", checks["unit manifests"].Detail, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Warn, checks["child repositories"].Status);
        Assert.Contains("services/orders/", checks["child repositories"].Detail, StringComparison.Ordinal);
        Assert.Equal(CheckStatus.Warn, checks["plugin scope"].Status);
        Assert.Contains("services/orders", checks["sdk pin"].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUserScopePluginAndAnIgnoredChildPass()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Write(Manifest.FileName, "version: 1\nunits:\n  - path: services/orders\n");
        Write(".gitignore", "services/\n");
        Directory.CreateDirectory(Path.Combine(home.FullName, ".claude"));
        File.WriteAllText(Path.Combine(home.FullName, ".claude", "settings.json"), """{ "enabledPlugins": { "lockflight@lockflight": true } }""");
        await GitAsync(root.FullName, ["init", "-q"]);
        Directory.CreateDirectory(Path.Combine(root.FullName, "services", "orders"));
        await GitAsync(Path.Combine(root.FullName, "services", "orders"), ["init", "-q"]);

        Dictionary<string, DoctorCheck> checks = (await WorkspaceChecks.RunAsync(root.FullName, ManifestLocator.Discover(root.FullName).Manifest, home.FullName, DateTimeOffset.UnixEpoch, cancellationToken))
            .ToDictionary(check => check.Name);

        Assert.Equal(CheckStatus.Pass, checks["child repositories"].Status);
        Assert.Equal(CheckStatus.Pass, checks["plugin scope"].Status);
        Assert.False(checks.ContainsKey("sdk pin"));
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(home);
    }
}
