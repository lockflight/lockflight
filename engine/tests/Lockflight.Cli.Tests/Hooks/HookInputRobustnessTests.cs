using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

[Collection(SerialPerformanceTests.Name)]
public sealed class HookInputRobustnessTests : IDisposable
{
    private const int HugeStringLength = 16 * 1024 * 1024;
    private const int ManyStrings = 200_000;
    private const int MaxMilliseconds = 10_000;
    private const string Approve = "/lockflight:approve spec";

    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-hook-input-");

    public HookInputRobustnessTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");

    [Fact]
    public async Task NestingDeeperThanTheParserAllowsBlocksTheToolCall()
    {
        string toolInput = new StringBuilder().Insert(0, "[", 10_000).Append("\"x\"").Insert(10_000 + 3, "]", 10_000).ToString();

        (ExitCode exitCode, _, string error) = await PreToolUseAsync("Agent", toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("hook input is not valid JSON", error, StringComparison.Ordinal);
        Assert.DoesNotContain("internal error", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheApproveCommandDeepInsideTheInputIsStillFound()
    {
        const int depth = 30;
        string toolInput = string.Concat(Enumerable.Repeat("{\"a\":[", depth)) + $"\"{Approve}\"" + string.Concat(Enumerable.Repeat("]}", depth));

        (ExitCode exitCode, _, string error) = await PreToolUseAsync("Agent", toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Only the person approves", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AHugeStringIsCheckedInBoundedTime(bool endsWithTheCommand)
    {
        string text = new string('x', HugeStringLength) + (endsWithTheCommand ? Approve : string.Empty);
        string toolInput = $"{{\"description\":\"d\",\"prompt\":\"{text}\"}}";
        Stopwatch watch = Stopwatch.StartNew();

        (ExitCode exitCode, _, string error) = await PreToolUseAsync("Agent", toolInput);

        Assert.InRange(watch.ElapsedMilliseconds, 0, MaxMilliseconds);
        Assert.Equal(endsWithTheCommand ? ExitCode.HookBlock : ExitCode.Pass, exitCode);
        Assert.DoesNotContain("internal error", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManySmallStringsAreCheckedInBoundedTime()
    {
        string items = string.Join(',', Enumerable.Range(0, ManyStrings).Select(index => $"\"item {index}\""));
        string toolInput = $"{{\"items\":[{items},\"{Approve}\"]}}";
        Stopwatch watch = Stopwatch.StartNew();

        (ExitCode exitCode, _, _) = await PreToolUseAsync("RemoteTrigger", toolInput);

        Assert.InRange(watch.ElapsedMilliseconds, 0, MaxMilliseconds);
        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Theory]
    [InlineData("""{"count":3,"ratio":1.5e300,"flag":true,"none":null,"nested":{"n":[1,2,{"m":false}]}}""")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"plain text\"")]
    [InlineData("""{"prompt":12345678901234567890123456789}""")]
    public async Task ValuesThatAreNotStringsPassWithoutAnError(string toolInput)
    {
        (ExitCode exitCode, _, string error) = await PreToolUseAsync("CronCreate", toolInput);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("\"/lockflight:approve spec\"")]
    [InlineData("""["/lockflight:approve spec"]""")]
    [InlineData("""{"/lockflight:approve spec":"x"}""")]
    [InlineData("""{"prompt":"\/lockflight:approve spec"}""")]
    [InlineData("""{"prompt":"/LOCKFLIGHT:APPROVE spec"}""")]
    public async Task TheApproveCommandIsFoundInAnyShapeOfInput(string toolInput)
    {
        (ExitCode exitCode, _, string error) = await PreToolUseAsync("ScheduleWakeup", toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Only the person approves", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACallWithoutAToolNameIsStillChecked()
    {
        string payload = $$$"""{"cwd":{{{Quoted(projectRoot.FullName)}}},"hook_event_name":"PreToolUse","tool_name":7,"tool_input":{"prompt":"{{{Approve}}}"}}""";

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Only the person approves", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolInputStringsHoldsEveryStringAndNameButNoOtherValue()
    {
        HookRequest input = ClaudeCodeAdapter.Parse("""{"tool_input":{"a":1,"b":[true,null,"s",{"c":"t"}],"d":2.5}}""", projectDirectory: null)!;

        Assert.Equal(["a", "b", "s", "c", "t", "d"], input.InputStrings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"tool_input\":")]
    [InlineData("{\"a\":1}{\"b\":2}")]
    public void InputThatIsNotOneJsonObjectIsNotParsed(string rawInput) =>
        Assert.Null(ClaudeCodeAdapter.Parse(rawInput, projectDirectory: null));

    public void Dispose() => TempDirectories.DeleteRecursively(projectRoot);

    private static string Quoted(string text) => JsonSerializer.Serialize(text);

    private Task<(ExitCode ExitCode, string Output, string Error)> PreToolUseAsync(string tool, string toolInput)
    {
        string payload = $$"""{"cwd":{{Quoted(projectRoot.FullName)}},"hook_event_name":"PreToolUse","tool_name":"{{tool}}","tool_input":{{toolInput}}}""";
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }
}
