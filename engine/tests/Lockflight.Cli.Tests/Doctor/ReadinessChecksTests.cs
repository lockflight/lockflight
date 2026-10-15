using Lockflight.Cli.Config;
using Lockflight.Cli.Doctor;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class ReadinessChecksTests : IDisposable
{
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
}
