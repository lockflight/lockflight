using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ledger;

public sealed class GateRunLedgerTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-gaterun-");

    [Fact]
    public async Task AGateRunFromTheCommandLineRecordsTheCommandTriggerItsDurationAndNoCache()
    {
        await CreateProjectAsync(stopGate: false);

        await TestConsole.RunAsync(["gate", "run", "--profile", "edit"], string.Empty, root.FullName);

        JsonElement completed = LastGateRun();
        Assert.StartsWith("gate:edit:", completed.GetProperty("signature").GetString(), StringComparison.Ordinal);
        Assert.Equal("command", completed.GetProperty("trigger").GetString());
        Assert.Equal("none", completed.GetProperty("cache").GetString());
        Assert.True(completed.GetProperty("durationMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task AStopHookThatRunsTheGateRecordsTheStopHookTriggerAndACacheMiss()
    {
        await CreateProjectAsync(stopGate: true);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        JsonElement completed = LastGateRun();
        Assert.StartsWith("gate:stop:FAIL:", completed.GetProperty("signature").GetString(), StringComparison.Ordinal);
        Assert.Equal("stop-hook", completed.GetProperty("trigger").GetString());
        Assert.Equal("miss", completed.GetProperty("cache").GetString());
        Assert.True(completed.GetProperty("durationMs").GetInt64() >= 0);
    }

    [Fact]
    public async Task AStopHookAnsweredFromTheCacheRecordsACacheHit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync(stopGate: true);
        string inputsHash = (await InputsHash.ComputeAsync(root.FullName, cancellationToken))!;
        await new StopGateCache(root.FullName, unitPath: null).RememberPassAsync(inputsHash, cancellationToken);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        JsonElement completed = LastGateRun();
        Assert.Equal("gate:stop:PASS:", completed.GetProperty("signature").GetString());
        Assert.Equal("stop-hook", completed.GetProperty("trigger").GetString());
        Assert.Equal("hit", completed.GetProperty("cache").GetString());
    }

    [Fact]
    public async Task AStopHookWithTheGateOffRecordsNoGateRun()
    {
        await CreateProjectAsync(stopGate: false);

        await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.False(Directory.Exists(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory)));
    }

    [Fact]
    public void TheGateRunFieldsAreReadBackAndOldLinesWithoutThemStillParse()
    {
        LedgerRecord current = LedgerReader.Parse(
            """{"ts":"2026-10-11T12:00:00+00:00","type":"gate.completed","signature":"gate:stop:PASS:","summary":"PASS","trigger":"stop-hook","durationMs":1250,"cache":"miss"}""")!;
        LedgerRecord old = LedgerReader.Parse("""{"ts":"2026-10-11T12:00:00+00:00","type":"gate.completed","signature":"gate:stop:PASS:","summary":"PASS"}""")!;

        Assert.Equal("stop-hook", current.Trigger);
        Assert.Equal(1250, current.DurationMs);
        Assert.Equal("miss", current.Cache);
        Assert.Null(old.Trigger);
        Assert.Null(old.DurationMs);
        Assert.Null(old.Cache);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync(bool stopGate)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(root.FullName, Manifest.FileName), $"version: 1\nstop:\n  gate: {(stopGate ? "true" : "false")}\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, ".gitignore"), ".lockflight/local/\n.lockflight/ledger/\n", cancellationToken);
        await ProcessRunner.RunAsync("git", ["add", "-A"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"], root.FullName, cancellationToken);
    }

    private string StopPayload() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["hook_event_name"] = "Stop",
        ["cwd"] = root.FullName,
        ["session_id"] = "s-1",
    });

    private JsonElement LastGateRun()
    {
        string ledger = Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory), "events-*.jsonl"));
        string line = File.ReadAllLines(ledger).Last(text => text.Contains("\"gate.completed\"", StringComparison.Ordinal));
        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }
}
