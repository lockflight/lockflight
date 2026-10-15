using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Tests.Ledger;

public sealed class LedgerTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-ledger-");

    public LedgerTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Fact]
    public async Task ABlockedWriteLeavesAFingerprintedLedgerLine()
    {
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "secrets", "key.txt"), projectRoot.FullName);

        await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        string[] files = Directory.GetFiles(Path.Combine(projectRoot.FullName, LedgerWriter.RelativeDirectory), "events-*.jsonl");
        string line = Assert.Single(File.ReadAllLines(Assert.Single(files)));
        using JsonDocument ledgerLine = JsonDocument.Parse(line);
        Assert.Equal("hook.blocked", ledgerLine.RootElement.GetProperty("type").GetString());
        Assert.Equal("pre-tool-use:protected-path:secrets/**", ledgerLine.RootElement.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task AnUnwritableLedgerNeverChangesTheDecision()
    {
        // A file where the ledger directory should be makes every append fail.
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, LedgerWriter.RelativeDirectory), "not a directory");
        string payload = HookPayloads.ToolCall("Write", Path.Combine(projectRoot.FullName, "secrets", "key.txt"), projectRoot.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
