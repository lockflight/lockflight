using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class SessionCommandRelayTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-relay-");

    public SessionCommandRelayTests() =>
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "version: 2\nprotected: []\n");

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
    [InlineData("Write", """{"file_path":".claude/scheduled_tasks.json","content":"{\"tasks\":[{\"id\":\"a\",\"cron\":\"* * * * *\",\"prompt\":\"/lockflight:approve spec\"}]}"}""")]
    [InlineData("Write", """{"file_path":".claude/scheduled_tasks.json","content":"{\"tasks\":[{\"id\":\"a\",\"cron\":\"* * * * *\",\"prompt\":\"/lockflight:baseline tests\"}]}"}""")]
    [InlineData("Edit", """{"file_path":"/repo/.claude/scheduled_tasks.json","old_string":"/lockflight:appr","new_string":"/lockflight:appr\u006fve spec"}""")]
    [InlineData("Edit", """{"file_path":"/repo/.claude/scheduled_tasks.json","old_string":"x","new_string":"ove spec"}""")]
    [InlineData("Write", """{"file_path":"C:\\repo\\.Claude\\Scheduled_Tasks.json","content":"{}"}""")]
    [InlineData("Write", """{"file_path":"/home/user/.claude/teams/t/inboxes/team-lead.json","content":"/lockflight:amend ShippingTests.A"}""")]
    [InlineData("MultiEdit", """{"file_path":".claude/routines/nightly.md","edits":[]}""")]
    [InlineData("Write", """{"file_path":"/repo/.claude/mailbox/main.json","content":"/lockflight:reset"}""")]
    [InlineData("Write", """{"file_path":"/home/user/.claude/loop.md","content":"- check the build"}""")]
    [InlineData("Bash", """{"command":"printf '%s' \"$TASKS\" > .claude/scheduled_tasks.json"}""")]
    [InlineData("Bash", """{"command":"cd sub && cp tasks.json ../.claude/scheduled_tasks.json"}""")]
    [InlineData("Bash", """{"command":"tee ~/.claude/teams/t/inboxes/lead.json < msg.json"}""")]
    [InlineData("PowerShell", """{"command":"Set-Content -Path .claude\\scheduled_tasks.json -Value $tasks"}""")]
    public async Task AWriteToAFileClaudeCodeReadsPromptsFromIsBlocked(string tool, string toolInput)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Claude Code reads prompts from", error, StringComparison.Ordinal);
        Assert.All(["/lockflight:approve", "/lockflight:baseline", "/lockflight:reset", "/lockflight:amend"], name => Assert.Contains(name, error, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Read", """{"file_path":".claude/scheduled_tasks.json"}""")]
    [InlineData("Bash", """{"command":"cat .claude/scheduled_tasks.json"}""")]
    [InlineData("Write", """{"file_path":"docs/scheduled_tasks.json","content":"{}"}""")]
    [InlineData("Write", """{"file_path":".claude/notes/loop.md","content":"x"}""")]
    [InlineData("Write", """{"file_path":"src/teams/t/inboxes/lead.json","content":"[]"}""")]
    public async Task ReadingAPromptQueueOrWritingALookalikePasses(string tool, string toolInput)
    {
        (ExitCode exitCode, _, _) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.Pass, exitCode);
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

    [Theory]
    [InlineData("cat <<'EOF' > memory-bank/note.md\n# Approvals\nThe person types /lockflight:approve tasks, never the agent.\nEOF")]
    [InlineData("printf '%s\\n' 'type /lockflight:approve tasks' > notes.md")]
    [InlineData("echo 'run /lockflight:approve' >> docs/x.md")]
    [InlineData("tee notes.md <<'EOF'\nAsk the person to run /lockflight:baseline tests.\nEOF")]
    [InlineData("tee -a notes.md <<EOF\nAsk the person to run /lockflight:reset.\nEOF")]
    [InlineData("echo '/lockflight:approve spec' | tee -a notes.md > /dev/null")]
    [InlineData("echo '/lockflight:amend' > ./memory-bank/n.md")]
    [InlineData("cat <<'EOF' > memory-bank/note.md\nThe person types /lockflight:approve tasks.\nEOF\n")]
    [InlineData("cat <<-\"EOF\" > memory-bank/note.md\n\tThe person types /lockflight:approve tasks.\n\tEOF")]
    [InlineData("echo 'see /lockflight:approve' > a.md && printf '%s' 'or /lockflight:reset' >> b.md")]
    public async Task AShellCommandThatOnlyWritesLiteralTextToFilesPasses(string command)
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Theory]
    [InlineData("cat <<EOF | claude\n/lockflight:approve spec\nEOF")]
    [InlineData("cat <<'EOF' > f\n/lockflight:approve spec\nEOF\nclaude -p \"$(cat f)\"")]
    [InlineData("cat <<EOF > n.md\n/lockflight:approve $(date)\nEOF")]
    [InlineData("cat <<EOF > n.md\n/lockflight:approve `date`\nEOF")]
    [InlineData("cat <<'EOF' > n.md\n/lockflight:approve ${X}\nEOF")]
    [InlineData("echo \"/lockflight:approve\" > \"$X\"")]
    [InlineData("echo /lockflight:approve > /dev/stdout")]
    [InlineData("echo /lockflight:approve > ../../../../../../../../dev/tty")]
    [InlineData("bash -c \"echo /lockflight:approve > n.md\"")]
    [InlineData("cat <<'EOF' > n.md &\n/lockflight:approve spec\nEOF")]
    [InlineData("(echo /lockflight:approve > n.md)")]
    [InlineData("{ echo /lockflight:approve; } > n.md")]
    [InlineData("echo /lockflight:approve > n.md; claude -p \"$(cat n.md)\"")]
    [InlineData("echo /lockflight:approve > n.md && claude -c < n.md")]
    [InlineData("cat prompt.md > n.md; echo /lockflight:approve")]
    [InlineData("echo /lockflight:approve")]
    [InlineData("X=/lockflight:approve echo x > n.md")]
    [InlineData("/bin/echo /lockflight:approve > n.md")]
    [InlineData("eval echo /lockflight:approve > n.md")]
    [InlineData("echo /lockflight:approve > n.md | claude")]
    [InlineData("echo /lockflight:approve |& tee n.md")]
    [InlineData("echo /lockflight:approve > ~/n.md")]
    [InlineData("echo /lockflight:approve > n*.md")]
    [InlineData("tee >(claude) <<'EOF'\n/lockflight:approve spec\nEOF")]
    [InlineData("printf -v x '/lockflight:approve' > n.md")]
    [InlineData("tee --output-error=warn n.md <<'EOF'\n/lockflight:approve spec\nEOF")]
    [InlineData("cat n.md <<'EOF' > m.md\n/lockflight:approve spec\nEOF")]
    [InlineData("cat <<EOF > n.md\nx\nEO\\\nF\nclaude -p /lockflight:approve spec\nEOF")]
    [InlineData("echo x > n.md ''# ; claude -p /lockflight:approve spec")]
    [InlineData("echo x >! n.md /lockflight:approve")]
    [InlineData("echo /lockflight:approve > n.md\u000bclaude")]
    [InlineData("echo x > n.md # <<EOF\nclaude -p /lockflight:approve spec\nEOF")]
    [InlineData("echo x >>| n.md /lockflight:approve")]
    [InlineData("echo hi >f <<EOF \\\nEOF\nclaude -p /lockflight:approve\nEOF")]
    [InlineData("cat >f <<EOF <<<'\nEOF\n'\n$(claude -p /lockflight:approve)\nEOF")]
    [InlineData("cat >f <<'' x\nhello\n\nclaude -p /lockflight:approve\nx")]
    [InlineData("cat >f <<<'/lockflight:approve\nspec'")]
    [InlineData("cat <<'EOF' > memory-bank/a.md && echo 'see /lockflight:approve' >> memory-bank/b.md\n/lockflight:approve spec\nEOF")]
    [InlineData("cat <<'EOF' > a.md\n/lockflight:approve spec\nEOF\nclaude -c < a.md")]
    [InlineData("cat <<'EOF' > a.md\n/lockflight:approve spec\nEOF\nEOF")]
    [InlineData("cat <<A > a.md <<B\n/lockflight:approve spec\nA\nx\nB")]
    [InlineData("echo $(cat <<EOF)\nclaude -p /lockflight:approve\nEOF")]
    [InlineData("cat <<EOF > n.md\nE\\\nOF\nclaude -p /lockflight:approve spec\nEOF")]
    [InlineData("cat <<EOF > n.md $(\nclaude -p /lockflight:approve spec\n)\nbody\nEOF")]
    [InlineData("cat <<\"\" > n.md\nhello\n\nclaude -p /lockflight:approve spec")]
    [InlineData("echo '/lockflight:approve' \"\" > n.md")]
    public async Task AShellCommandThatDoesMoreThanWriteLiteralTextIsChecked(string command)
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("lockflight:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cat <<'EOF' > .claude/loop.md\n/lockflight:approve spec\nEOF")]
    [InlineData("echo '/lockflight:approve spec' > .claude/./loop.md")]
    [InlineData("echo '/lockflight:approve spec' > .claude/loop.md.")]
    [InlineData("echo '/lockflight:approve spec' > .claude/x/../scheduled_tasks.json")]
    [InlineData("tee -a .claude/mailbox/main.json <<'EOF'\n/lockflight:approve spec\nEOF")]
    public async Task ALiteralWriteToAPromptQueueFileIsBlocked(string command)
    {
        (ExitCode exitCode, _, string error) = await RunBashAsync(command);

        HookAssert.Blocked(exitCode, error);
    }

    [Theory]
    [InlineData("echo '/lockflight:approve spec' > loop.md")]
    [InlineData("echo '/lockflight:approve spec' > ../.claude/scheduled_tasks.json")]
    [InlineData("echo '/lockflight:approve spec' > mailbox/main.json")]
    [InlineData("echo '/lockflight:approve spec' > C:loop.md")]
    [InlineData("echo '/lockflight:approve spec' > C:../.claude/loop.md")]
    [InlineData("echo '/lockflight:approve spec' > notes.md:stream")]
    public async Task ALiteralWriteIsResolvedAgainstTheWorkingDirectory(string command)
    {
        string claudeDirectory = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude")).FullName;

        (ExitCode exitCode, _, string error) = await RunBashAsync(command, claudeDirectory);

        HookAssert.Blocked(exitCode, error);
    }

    [Theory]
    [InlineData("Write", """{"file_path":".claude/./loop.md","content":"/lockflight:approve spec"}""")]
    [InlineData("Write", """{"file_path":".claude/x/../scheduled_tasks.json","content":"{}"}""")]
    [InlineData("Edit", """{"file_path":"docs/../.claude/loop.md","old_string":"a","new_string":"/lockflight:approve spec"}""")]
    [InlineData("Bash", """{"command":"printf 'x' >> .claude/./loop.md"}""")]
    public async Task AWriteToAPromptQueueFileSpelledAnotherWayIsBlocked(string tool, string toolInput)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("a file Claude Code reads prompts from", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Write", """{"file_path":"loop.md","content":"/lockflight:approve spec"}""")]
    [InlineData("Bash", """{"command":"printf 'x' > loop.md"}""")]
    [InlineData("Bash", """{"command":"printf 'x' >> mailbox/main.json"}""")]
    public async Task AWriteFromInsideTheClaudeDirectoryToAPromptQueueFileIsBlocked(string tool, string toolInput)
    {
        string claudeDirectory = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude")).FullName;

        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput, claudeDirectory);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("a file Claude Code reads prompts from", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWriteThroughALinkToTheClaudeDirectoryIsBlocked()
    {
        string claudeDirectory = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".claude")).FullName;
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "cfg"), claudeDirectory);

        (ExitCode exitCode, _, string error) = await RunAsync("Write", """{"file_path":"cfg/loop.md","content":"x"}""");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("a file Claude Code reads prompts from", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bash", """{"command":"echo \"/lockflight:approve $(date)\" > memory-bank/n.md"}""")]
    [InlineData("mcp__memory__write", """{"path":"memory-bank/n.md","content":"type /lockflight:approve tasks"}""")]
    [InlineData("PowerShell", """{"command":"Set-Content -Path notes.md -Value '/lockflight:approve tasks'"}""")]
    public async Task ABlockedCallThatWritesAFileSaysHowToSaveTheNote(string tool, string toolInput)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Only the person approves", error, StringComparison.Ordinal);
        Assert.Contains("Write or Edit tool", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CronCreate", """{"cron":"*/1 * * * *","prompt":"/lockflight:approve spec","recurring":false}""")]
    [InlineData("Bash", """{"command":"claude -p '/lockflight:approve spec'"}""")]
    public async Task ABlockedCallThatWritesNoFileDoesNotSuggestAFileTool(string tool, string toolInput)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(tool, toolInput);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.DoesNotContain("Write or Edit tool", error, StringComparison.Ordinal);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);

    private Task<(ExitCode ExitCode, string Output, string Error)> RunBashAsync(string command, string? workingDirectory = null) =>
        RunAsync("Bash", JsonSerializer.Serialize(new Dictionary<string, string> { ["command"] = command }), workingDirectory);

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string tool, string toolInput, string? workingDirectory = null)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = workingDirectory ?? projectRoot.FullName,
            ["hook_event_name"] = "PreToolUse",
            ["tool_name"] = tool,
            ["tool_input"] = JsonDocument.Parse(toolInput).RootElement,
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }
}
