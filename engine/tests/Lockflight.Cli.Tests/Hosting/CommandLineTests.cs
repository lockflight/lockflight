using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class CommandLineTests : IDisposable
{
    private readonly DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("lockflight-cli-");

    [Theory]
    [InlineData("--help")]
    [InlineData("gate", "run", "--help")]
    [InlineData("approve", "spec", "-h")]
    public async Task HelpAnywhereOnTheCommandLinePrintsTheUsageAndSucceeds(params string[] args)
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(args, "", workingDirectory.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("Exit codes", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheUsageListsEveryHookEventAndTheVerifyOptions()
    {
        (_, string output, _) = await TestConsole.RunAsync(["--help"], "", workingDirectory.FullName);

        foreach (HookEvent hookEvent in Enum.GetValues<HookEvent>().Where(hookEvent => hookEvent != HookEvent.Unknown))
        {
            Assert.Contains(HookEventNames.ToName(hookEvent), output, StringComparison.Ordinal);
        }

        Assert.Contains("approve verify (--feature <id> | --base <ref>)", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoArgumentsPrintTheUsageAsAConfigurationError()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync([], "", workingDirectory.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("Usage:", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("gate", "frobnicate")]
    [InlineData("ledger", "frobnicate")]
    public async Task AnUnknownCommandOrVerbIsAConfigurationErrorThatPointsToTheHelp(params string[] args)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(args, "", workingDirectory.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains($"unknown command '{string.Join(' ', args)}'", error, StringComparison.Ordinal);
        Assert.Contains("--help", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown profile 'bogus'; use lite or standard", "--profile", "bogus")]
    [InlineData("--profile needs a value", "--profile")]
    [InlineData("unknown init option '--force'", "--force")]
    public async Task InitNamesTheOptionItCannotUse(string expected, params string[] options)
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["init", .. options], "", workingDirectory.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains(expected, error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(workingDirectory.FullName, Manifest.FileName)));
    }

    public void Dispose() => workingDirectory.Delete(recursive: true);
}
