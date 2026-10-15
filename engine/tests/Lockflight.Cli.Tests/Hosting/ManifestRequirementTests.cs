using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class ManifestRequirementTests : IDisposable
{
    private const string InvalidManifest = "protected:\n  - secrets/**\nunknown: 1\n";
    private const string ParseErrorPosition = "lockflight.yaml:3:1";

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-manifest-");

    [Theory]
    [InlineData("phase", "check", "specify")]
    [InlineData("redgreen", "status")]
    [InlineData("trace")]
    [InlineData("review")]
    [InlineData("ledger", "retro")]
    [InlineData("approve", "verify", "--base", "main")]
    public async Task ALifecycleCommandWithoutAManifestSaysToRunInit(params string[] args)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(args, "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("run 'dotnet lockflight init' first", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("phase", "check", "specify")]
    [InlineData("redgreen", "status")]
    [InlineData("trace")]
    [InlineData("review")]
    [InlineData("ledger", "retro")]
    [InlineData("approve", "verify", "--base", "main")]
    [InlineData("link")]
    public async Task ALifecycleCommandWithAnInvalidManifestShowsTheParseErrorInsteadOfAskingForInit(params string[] args)
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), InvalidManifest);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(args, "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(ParseErrorPosition, error, StringComparison.Ordinal);
        Assert.DoesNotContain("lockflight init", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GateRunRefusesAnInvalidManifestInsteadOfFallingBackToDefaults()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), InvalidManifest);

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["gate", "run"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(ParseErrorPosition, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Result:", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveVerifyNeedsAFeatureOrABase()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), Templates.Manifest(InitProfile.Lite));

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["approve", "verify"], "", projectRoot.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--feature <id> or --base <ref>", error, StringComparison.Ordinal);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
