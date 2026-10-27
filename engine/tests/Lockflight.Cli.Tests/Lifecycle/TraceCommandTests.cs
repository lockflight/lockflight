using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class TraceCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-trace-");

    [Fact]
    public async Task ASpecWithoutRequirementsHasNothingToTrace()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "001-orders"));
        File.WriteAllText(Path.Combine(root.FullName, "specs", "001-orders", "spec.md"), "# Feature Specification: Orders\n\nNo requirements yet.\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["trace", "--feature", "001-orders"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.NotEarned, exitCode);
        Assert.Contains("NOT-EARNED", output, StringComparison.Ordinal);
        Assert.Contains("no requirements", output, StringComparison.Ordinal);
    }

    public void Dispose() => root.Delete(recursive: true);
}
