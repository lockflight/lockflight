using System.Globalization;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class TerminalOutputSafetyTests : IDisposable
{
    private const string Escape = "\u001b";
    private const string Bell = "\u0007";

    /// <summary>Sets the clipboard (OSC 52) and clears the screen: what a hostile name would try.</summary>
    private const string Hostile = "\u001b]52;c;cHduZWQ=\u0007\u001b[2J";

    private const string HostileShown = "^[]52;c;cHduZWQ=^G^[[2J";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-terminal-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("tab\tand\nline feed", "tab\tand\nline feed")]
    [InlineData("windows\r\nline", "windows\r\nline")]
    [InlineData("PASS\rFAIL", "PASS^MFAIL")]
    [InlineData("ends with\r", "ends with^M")]
    [InlineData("\u001b[31mred\u001b[0m", "^[[31mred^[[0m")]
    [InlineData("\u001b]0;title\u0007", "^[]0;title^G")]
    [InlineData("back\bspace\0nul", "back^Hspace^@nul")]
    [InlineData("del\u007f", "del^?")]
    [InlineData("c1 \u009b31m csi", "c1 <U+009B>31m csi")]
    [InlineData("fdp.‮txt", "fdp.<U+202E>txt")]
    [InlineData("isolate⁦x⁩", "isolate<U+2066>x<U+2069>")]
    [InlineData("café 中文 \U0001F600", "café 中文 \U0001F600")]
    public void TerminalTextShowsEveryControlSequenceVisibly(string text, string expected) =>
        Assert.Equal(expected, TerminalText.Clean(text));

    [Fact]
    public void PlainTextIsReturnedAsItIs()
    {
        string text = "nothing to clean";

        Assert.Same(text, TerminalText.Clean(text));
    }

    [Theory]
    [InlineData("src/App.cs", true)]
    [InlineData("docs/café notes.md", true)]
    [InlineData("a\nb", false)]
    [InlineData("a\tb", false)]
    [InlineData("x\u001b[0m", false)]
    [InlineData("txt.‮exe", false)]
    public void APlainNameHasNoControlCharacterAtAll(string name, bool plain) =>
        Assert.Equal(plain, TerminalText.IsPlainName(name));

    [Fact]
    public async Task TheSafeWriterCleansEveryWayOfWritingAndKeepsTheTerminalsLineEnd()
    {
        using StringWriter terminal = new() { NewLine = "\r\n" };
        using TerminalSafeWriter writer = new(terminal);

        writer.Write('\u001b');
        writer.Write("a\u0007");
        writer.Write("b\u0008".AsSpan());
        writer.WriteLine("c\rd");
        await writer.WriteAsync("e\u001b".AsMemory(), TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("f\u009b".AsMemory(), TestContext.Current.CancellationToken);
        await writer.WriteLineAsync("g\u007f");
        writer.WriteLine();

        Assert.Equal("^[a^Gb^Hc^Md\r\ne^[f<U+009B>\r\ng^?\r\n\r\n", terminal.ToString());
    }

    [Fact]
    public async Task EveryCommandPrintsThroughTheSafeWriter()
    {
        (_, string output, string error) = await TestConsole.RunAsync(["gate", "run", "--profile", Hostile], string.Empty, root.FullName, time);

        AssertClean(output + error);
        Assert.Contains(HostileShown, output + error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownCommandIsEchoedWithoutItsEscapes()
    {
        (_, _, string error) = await TestConsole.RunAsync([Hostile], string.Empty, root.FullName, time);

        AssertClean(error);
        Assert.Contains(HostileShown, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheStatusLineShowsABlockedNameWithoutItsEscapes(bool color)
    {
        WriteManifest();
        WriteLedger($$"""{"ts":"{{Recently()}}","type":"hook.blocked","signature":"pre-tool-use:protected-path:x","summary":{{JsonSerializer.Serialize("protected-path:" + Hostile)}}}""");
        Dictionary<string, string> environment = color ? [] : new() { [EnvironmentVariables.NoColor] = "1" };

        (_, string output, _) = await TestConsole.RunAsync(["statusline"], Session(), "/", environment, time);

        Assert.Contains(HostileShown, output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b]52", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[2J", output, StringComparison.Ordinal);
        Assert.DoesNotContain(Bell, output, StringComparison.Ordinal);
        Assert.Equal(color, output.Contains(Escape, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AHookBlockNamesTheFileWithoutItsEscapes()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        string payload = HookPayloads.ToolCall("Write", Path.Combine(root.FullName, "secrets", $"key{Hostile}.txt"), root.FullName);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, root.FullName, time);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        AssertClean(error);
        Assert.Contains(HostileShown, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRetroShowsLedgerFieldsWithoutTheirEscapes()
    {
        WriteManifest();
        string line = $$"""{"ts":"{{Recently()}}","type":"hook.blocked","signature":{{JsonSerializer.Serialize("pre-tool-use:" + Hostile)}},"summary":{{JsonSerializer.Serialize(Hostile)}},"detail":{{JsonSerializer.Serialize(Hostile)}}}""";
        WriteLedger(line, line, line);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["ledger", "retro"], string.Empty, root.FullName, time);

        Assert.Equal(ExitCode.Pass, exitCode);
        AssertClean(output);
        Assert.Contains("^[", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APhaseCheckShowsAFeatureNameWithoutItsEscapes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows does not allow control characters in directory names.");
        WriteManifest();
        string feature = $"001-x{Escape}[8m";
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", feature));
        File.WriteAllText(Path.Combine(root.FullName, "specs", feature, "spec.md"), $"# Spec\n\n- **FR-001**: {Hostile}\n");

        (_, string output, string error) = await TestConsole.RunAsync(["phase", "check", "specify"], string.Empty, root.FullName, time);

        AssertClean(output + error);
        Assert.Contains("001-x^[[8m", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSessionApprovalDigestShowsSpecTextWithoutItsEscapes()
    {
        WriteManifest();
        string spec = Path.Combine(root.FullName, "specs", "001-orders", "spec.md");
        Directory.CreateDirectory(Path.GetDirectoryName(spec)!);
        File.WriteAllText(spec, $"# Orders\n\n### User Story 1 - Pay{Hostile} (Priority: P1)\n\n- **FR-001**: Pay\n");
        string payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["cwd"] = root.FullName,
            ["hook_event_name"] = "UserPromptExpansion",
            ["command_name"] = "lockflight:approve",
            ["command_args"] = "spec",
            ["command_source"] = "plugin",
        });
        Dictionary<string, string> attended = new()
        {
            [EnvironmentVariables.ClaudeCodeEntrypoint] = "cli",
            [EnvironmentVariables.ClaudeCodeSessionAttended] = "1",
        };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "user-prompt-expansion"], payload, root.FullName, attended, time);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("you are about to approve the spec", error, StringComparison.Ordinal);
        AssertClean(error);
        Assert.Contains($"1 Pay{HostileShown}", error, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static void AssertClean(string text)
    {
        Assert.DoesNotContain(Escape, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Bell, text, StringComparison.Ordinal);
    }

    private string Recently() => time.Now.AddMinutes(-5).ToString("O", CultureInfo.InvariantCulture);

    private void WriteManifest() => File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

    private void WriteLedger(params string[] lines)
    {
        string ledger = Path.Combine(root.FullName, ".lockflight", "ledger");
        Directory.CreateDirectory(ledger);
        File.WriteAllLines(Path.Combine(ledger, $"events-{time.Now:yyyy-MM}.jsonl"), lines);
    }

    private string Session() => JsonSerializer.Serialize(new Dictionary<string, object> { ["workspace"] = new Dictionary<string, string> { ["project_dir"] = root.FullName } });
}
