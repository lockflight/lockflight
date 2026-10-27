using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Mascot;

public sealed class StatusLineCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-statusline-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task OutsideALockflightProjectItPrintsNothing()
    {
        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), root.FullName, time);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(output);
    }

    [Fact]
    public async Task WithNoRecentVerdictLeviIsOnDuty()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", Plain(), time);

        Assert.Equal("Lockflight · on duty", output);
    }

    [Theory]
    [InlineData("hook.blocked", "pre-tool-use:protected-path:**/.env", "protected-path:**/.env", "guard blocked **/.env")]
    [InlineData("hook.blocked", "stop:stop-gate", "stop-gate", "Stop gate FAIL")]
    [InlineData("gate.completed", "gate:pr:NOT-EARNED:test", "NOT-EARNED", "pr gate NOT-EARNED")]
    [InlineData("review.completed", "review:changes", "001-orders: 1 open confirmed", "review FAIL")]
    [InlineData("review.completed", "review:clean", "001-orders: clean", "review PASS")]
    [InlineData("review.completed", "review:not-of-record", "001-orders: not of record", "review NOT-EARNED")]
    public async Task TheLastVerdictIsShown(string type, string signature, string summary, string expected)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        string at = time.Now.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(ledger, $"events-{time.Now:yyyy-MM}.jsonl"),
            $$"""{"schema":"lockflight.ledger/v1","id":"1","ts":"{{at}}","type":"{{type}}","signature":"{{signature}}","summary":"{{summary}}","detail":null}""" + "\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", Plain(), time);

        Assert.Equal($"Lockflight · {expected} · 5m ago", output);
    }

    [Theory]
    [InlineData("approval.recorded", "approve:spec", "001-orders spec approved")]
    [InlineData("finding.dismissed", "finding:dismissed", "001-orders 0123456789ab dismissed")]
    [InlineData("finding.fixed", "finding:fixed", "001-orders 0123456789ab fixed")]
    [InlineData("gate.completed", "gate:stop-unit:services/orders", "1250")]
    public async Task EventsThatAreNotVerdictsLeaveTheLastVerdictShown(string type, string signature, string summary)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        string earlier = time.Now.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        string later = time.Now.AddMinutes(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(ledger, $"events-{time.Now:yyyy-MM}.jsonl"),
            $$"""{"ts":"{{earlier}}","type":"gate.completed","signature":"gate:stop:PASS:","summary":"PASS","trigger":"stop-hook","durationMs":900,"cache":"miss"}""" + "\n"
            + $$"""{"ts":"{{later}}","type":"{{type}}","signature":"{{signature}}","summary":"{{summary}}"}""" + "\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", Plain(), time);

        Assert.Equal("Lockflight · stop gate PASS · 5m ago", output);
    }

    [Fact]
    public async Task InColorItDrawsLevisFaceOnTwoLines()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", time);

        Assert.Equal(2, output.Split('\n').Length);
        Assert.Contains("▀", output, StringComparison.Ordinal);
    }

    private string Session() => "{\"cwd\":\"/elsewhere\",\"workspace\":{\"project_dir\":" + System.Text.Json.JsonSerializer.Serialize(root.FullName) + "}}";

    private static Dictionary<string, string> Plain() => new() { [EnvironmentVariables.NoColor] = "1" };

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
