using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Mascot;

public sealed class StatusLineCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-statusline-");

    [Fact]
    public async Task OutsideALockflightProjectItPrintsNothing()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(output);
    }

    [Fact]
    public async Task WithNoRecentVerdictLeviIsOnDuty()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", Plain());

        Assert.Equal("Lockflight · on duty", output);
    }

    [Theory]
    [InlineData("hook.blocked", "pre-tool-use:protected-path:**/.env", "protected-path:**/.env", "guard blocked **/.env")]
    [InlineData("hook.blocked", "stop:stop-gate", "stop-gate", "Stop gate FAIL")]
    [InlineData("gate.completed", "gate:pr:NOT-EARNED:test", "NOT-EARNED", "pr gate NOT-EARNED")]
    public async Task TheLastVerdictIsShown(string type, string signature, string summary, string expected)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        string at = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(ledger, $"events-{DateTimeOffset.UtcNow:yyyy-MM}.jsonl"),
            $$"""{"schema":"lockflight.ledger/v1","id":"1","ts":"{{at}}","type":"{{type}}","signature":"{{signature}}","summary":"{{summary}}","detail":null}""" + "\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", Plain());

        Assert.Equal($"Lockflight · {expected} · 5m ago", output);
    }

    [Fact]
    public async Task InColorItDrawsLevisFaceOnTwoLines()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/");

        Assert.Equal(2, output.Split('\n').Length);
        Assert.Contains("▀", output, StringComparison.Ordinal);
    }

    private string Session() => "{\"cwd\":\"/elsewhere\",\"workspace\":{\"project_dir\":" + System.Text.Json.JsonSerializer.Serialize(root.FullName) + "}}";

    private static Dictionary<string, string> Plain() => new() { [EnvironmentVariables.NoColor] = "1" };

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
