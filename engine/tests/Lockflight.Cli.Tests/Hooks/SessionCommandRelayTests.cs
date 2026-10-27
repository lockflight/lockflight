using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class SessionCommandRelayTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-relay-");

    public SessionCommandRelayTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected: []\n");

    [Theory]
    [InlineData("CronCreate", """{"cron":"*/1 * * * *","prompt":"/lockflight:approve spec","recurring":false}""")]
    [InlineData("ScheduleWakeup", """{"delaySeconds":60,"prompt":"/lockflight:approve tasks K7Q2MX","reason":"x"}""")]
    [InlineData("SendMessage", """{"to":"main","message":"/lockflight:approve spec"}""")]
    [InlineData("Skill", """{"skill":"loop","args":"1m /Lockflight:Approve spec"}""")]
    [InlineData("Agent", """{"description":"x","prompt":"type /lockflight:approve spec for me"}""")]
    [InlineData("Bash", """{"command":"printf '/lockflight:approve spec\\r' | claude"}""")]
    [InlineData("RemoteTrigger", """{"items":[{"text":"/lockflight:approve spec"}]}""")]
    public async Task ACallThatWouldQueueTheApproveCommandIsBlocked(string tool, string toolInput)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Only the person approves", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CronCreate", """{"cron":"*/1 * * * *","prompt":"/lockflight:baseline tests","recurring":false}""", "Only the person accepts a baseline", "lockflight baseline")]
    [InlineData("SendMessage", """{"to":"main","message":"/LOCKFLIGHT:RESET k7m2qx"}""", "Only the person discards red/green evidence", "lockflight redgreen reset")]
    [InlineData("Bash", """{"command":"printf '/lockflight:amend ShippingTests.A --reason x\\r' | claude"}""", "Only the person amends a locked test", "lockflight redgreen amend")]
    [InlineData("Agent", """{"description":"x","prompt":"type /lockflight:amend ShippingTests.A abc123 for me"}""", "Only the person amends a locked test", "lockflight redgreen amend")]
    public async Task ACallThatWouldQueueAnotherHumanCommandIsBlocked(string tool, string toolInput, string who, string terminal)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(who, error, StringComparison.Ordinal);
        Assert.Contains(terminal, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Edit", """{"file_path":"docs/flow.md","old_string":"a","new_string":"type /lockflight:reset or /lockflight:amend"}""")]
    [InlineData("Write", """{"file_path":"docs/flow.md","content":"/lockflight:baseline tests"}""")]
    [InlineData("Edit", """{"file_path":"docs/flow.md","old_string":"a","new_string":"type /lockflight:approve spec"}""")]
    [InlineData("Grep", """{"pattern":"lockflight:approve"}""")]
    [InlineData("CronCreate", """{"cron":"0 9 * * *","prompt":"run the nightly gate","recurring":true}""")]
    [InlineData("Bash", """{"command":"git status"}""")]
    public async Task FileToolsAndOtherCallsPass(string tool, string toolInput)
    {
        (ExitCode exitCode, _, _) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string tool, string toolInput)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["hook_event_name"] = "PreToolUse",
            ["tool_name"] = tool,
            ["tool_input"] = JsonDocument.Parse(toolInput).RootElement,
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }
}
