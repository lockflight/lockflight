using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class InvalidManifestHookTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-invalid-");

    public InvalidManifestHookTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\nunknown: 1\n");

    [Fact]
    public async Task WithAnInvalidManifestWritesAreBlockedWithTheParseError()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("lockflight.yaml:3:1", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnInvalidManifestReadingIsStillAllowed()
    {
        string payload = HookPayloads.ToolCall("Read", Path.Combine(projectRoot.FullName, "src", "a.cs"), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
