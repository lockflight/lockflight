using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class ReadinessChecksTests : IDisposable
{
    private const string DevelopmentVersion = "0.2.0-dev.20260101000000";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-doctor-");

    [Fact]
    public async Task AStandardProfileWithoutTheLockflightPresetFails()
    {
        Write(".gitattributes", GitAttributesUpdater.Rule + "\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));

        DoctorCheck preset = await CheckAsync("lockflight preset");

        Assert.Equal(CheckStatus.Fail, preset.Status);
        Assert.Contains("specify preset add", preset.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInstalledPresetInSpecKitsRegistryPasses()
    {
        Write(".gitattributes", GitAttributesUpdater.Rule + "\n");
        Write(".specify/presets/.registry", """{ "schema_version": "1.0", "presets": { "lockflight": { "version": "0.1.0", "enabled": true, "priority": 10 } } }""");

        DoctorCheck preset = await CheckAsync("lockflight preset");

        Assert.Equal(CheckStatus.Pass, preset.Status);
        Assert.Contains("0.1.0", preset.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALiteProjectsOwnSpecKitGetsNoPresetChecks()
    {
        Write(".specify/presets/.registry", """{ "presets": { "lockflight": { "version": "0.1.0", "enabled": false } } }""");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(checks, check => check.Name is "lockflight preset" or "spec kit presets");
    }

    [Fact]
    public async Task ACoverageCollectorWithoutItsPackageInATestProjectFails()
    {
        Write(Manifest.FileName, "version: 1\ngates:\n  coverage:\n    collector: microsoft\n");
        Write("global.json", """{ "test": { "runner": "Microsoft.Testing.Platform" } }""");
        Write("tests/A.Tests/A.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/B.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit.v3.mtp-v2" Version="4.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/Directory.Build.props", """<Project><ItemGroup><PackageReference Include="Microsoft.Testing.Extensions.CodeCoverage" Version="18.0.4" /></ItemGroup></Project>""");

        DoctorCheck coverage = await CheckAsync("coverage collector");

        Assert.Equal(CheckStatus.Fail, coverage.Status);
        Assert.Contains("Microsoft.Testing.Extensions.CodeCoverage", coverage.Detail, StringComparison.Ordinal);
        Assert.Contains("tests/A.Tests/A.Tests.csproj", coverage.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("B.Tests", coverage.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnVSTestTheCoverletCollectorIsTheExpectedPackage()
    {
        Write(Manifest.FileName, "version: 1\ngates:\n  coverage:\n    collector: coverlet\n");
        Write("tests/A.Tests/A.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.2" /><PackageReference Include="coverlet.collector" Version="6.0.2" /></ItemGroup></Project>""");
        Write("tests/B.Tests/B.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.2" /></ItemGroup></Project>""");

        DoctorCheck coverage = await CheckAsync("coverage collector");

        Assert.Equal(CheckStatus.Fail, coverage.Status);
        Assert.Contains("tests/B.Tests/B.Tests.csproj (coverlet.collector)", coverage.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("A.Tests", coverage.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoCoverageCheckRunsWithoutACollector()
    {
        Write(Manifest.FileName, "version: 1\n");

        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(checks, check => check.Name == "coverage collector");
    }

    [Fact]
    public async Task AMissingPythonIsAWarningWithItsFixNotACrash()
    {
        DoctorCheck check = await ReadinessChecks.CheckPyYamlAsync(root.FullName, "lockflight-no-such-python", TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("python3 not found", check.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingBashFailsWithItsFixNotACrash()
    {
        DoctorCheck check = await ReadinessChecks.CheckGitBashAsync(root.FullName, "lockflight-no-such-bash", TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("Git for Windows", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentPinOnNoLocalFeedAndNotInTheToolsCacheFails()
    {
        PinEngine(DevelopmentVersion);
        string home = HomeWithUserInstall();

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, home, nugetPackages: null)!;

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains(DevelopmentVersion, check.Detail, StringComparison.Ordinal);
        Assert.Contains("~/.lockflight/bin/lockflight update", check.Fix, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnInstallFromSourceTheFixIsToInstallOne()
    {
        PinEngine(DevelopmentVersion);

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null)!;

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("scripts/dev-install.sh", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentPinOnlyInTheLocalFeedWarnsThatAPlainRestoreCannotGetIt()
    {
        PinEngine(DevelopmentVersion);
        string home = HomeWithUserInstall();
        Write($"home/.lockflight/feed/Lockflight.Cli.{DevelopmentVersion}.nupkg", string.Empty);

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, home, nugetPackages: null)!;

        Assert.Equal(CheckStatus.Warn, check.Status);
        Assert.Contains("--add-source", check.Fix, StringComparison.Ordinal);
    }

    [Fact]
    public void ADevelopmentPinInTheToolsCachePasses()
    {
        PinEngine(DevelopmentVersion);
        Directory.CreateDirectory(Path.Combine(root.FullName, "packages", "lockflight.cli", DevelopmentVersion));

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), Path.Combine(root.FullName, "packages"))!;

        Assert.Equal(CheckStatus.Pass, check.Status);
    }

    [Fact]
    public void TheToolsCacheDefaultsToTheNuGetFolderInTheHome()
    {
        PinEngine(DevelopmentVersion);
        Directory.CreateDirectory(Path.Combine(root.FullName, "home", ".nuget", "packages", "lockflight.cli", DevelopmentVersion));

        DoctorCheck check = ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null)!;

        Assert.Equal(CheckStatus.Pass, check.Status);
    }

    [Fact]
    public void AReleasePinIsNotLookedUpOnAnyFeed()
    {
        PinEngine("0.2.0-alpha.2");

        Assert.Null(ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null));
    }

    [Fact]
    public void WithoutAPinThereIsNoRestoreCheck() =>
        Assert.Null(ReadinessChecks.CheckPinRestorable(root.FullName, Path.Combine(root.FullName, "home"), nugetPackages: null));

    public void Dispose() => root.Delete(recursive: true);

    private async Task<DoctorCheck> CheckAsync(string name)
    {
        IReadOnlyList<DoctorCheck> checks = await ReadinessChecks.RunAsync(root.FullName, TestContext.Current.CancellationToken);
        return Assert.Single(checks, check => check.Name == name);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void PinEngine(string version) =>
        Write("dotnet-tools.json", $$"""{ "version": 1, "isRoot": true, "tools": { "lockflight.cli": { "version": "{{version}}", "commands": ["lockflight"] } } }""");

    private string HomeWithUserInstall()
    {
        Write("home/.lockflight/bin/lockflight", string.Empty);
        return Path.Combine(root.FullName, "home");
    }
}
