using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class TaskCompletedHookTests : IDisposable
{
    /// <summary>A unit that is guarded but not built: the stop profile judges it N/A without a build, so a gate run passes fast.</summary>
    private const string GuardedOnly = "version: 1\nstop:\n  gate: true\nunits:\n  - path: web\n    kind: none\n";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-taskcompleted-");

    [Fact]
    public async Task ATaskIsNotCompletedBeforeTheStopGatePassedForTheTree()
    {
        await CreateProjectAsync(GuardedOnly);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("task \"Add login\" is not completed", error, StringComparison.Ordinal);
        Assert.Contains("gate run", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATaskIsCompletedOnceAGateRunPassedForTheTree()
    {
        await CreateProjectAsync(GuardedOnly);

        (ExitCode gate, string output, _) = await TestConsole.RunAsync(["gate", "run", "--profile", "stop"], string.Empty, root.FullName);
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);

        Assert.True(gate == ExitCode.Pass, output);
        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task AChangeAfterThePassBlocksTheTaskAgain()
    {
        await CreateProjectAsync(GuardedOnly);
        await TestConsole.RunAsync(["gate", "run", "--profile", "stop"], string.Empty, root.FullName);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "web", "index.html"), "<p>changed</p>", TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task AGateRunOfAnotherProfileCachesNoPass()
    {
        await CreateProjectAsync(GuardedOnly);

        await TestConsole.RunAsync(["gate", "run", "--profile", "edit"], string.Empty, root.FullName);
        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Fact]
    public async Task TheTaskIsNotBlockedAgainOnATreeTheStopGateAlreadySentBackInThisSession()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync("version: 1\nstop:\n  gate: true\n");
        string inputsHash = (await InputsHash.ComputeAsync(root.FullName, cancellationToken))!;
        await new StopBlockRecord(root.FullName).RememberAsync("s-1", inputsHash, cancellationToken);

        (ExitCode sameSession, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);
        (ExitCode otherSession, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-2"), root.FullName);

        Assert.Equal(ExitCode.Pass, sameSession);
        Assert.Equal(ExitCode.HookBlock, otherSession);
    }

    [Fact]
    public async Task AStopGateBlockIsRecordedForTheSessionAndAPassClearsIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync("version: 1\nstop:\n  gate: true\n");
        StopBlockRecord record = new(root.FullName);

        (ExitCode stop, _, _) = await TestConsole.RunAsync(["hook", "stop"], Payload("s-1"), root.FullName);
        string inputsHash = (await InputsHash.ComputeAsync(root.FullName, cancellationToken))!;
        bool blocked = await record.BlockedAsync("s-1", inputsHash, cancellationToken);
        await new StopGateCache(root.FullName, unitPath: null).RememberPassAsync(inputsHash, cancellationToken);
        await TestConsole.RunAsync(["hook", "stop"], Payload("s-1"), root.FullName);

        Assert.Equal(ExitCode.HookBlock, stop);
        Assert.True(blocked);
        Assert.False(await record.BlockedAsync("s-1", inputsHash, cancellationToken));
    }

    [Fact]
    public async Task WithTheStopGateOffATaskIsAlwaysCompleted()
    {
        await CreateProjectAsync("version: 1\n");

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "task-completed"], Payload("s-1"), root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public void TheEventNameRoundTrips() =>
        Assert.Equal(HookEvent.TaskCompleted, HookEventNames.Parse(HookEventNames.ToName(HookEvent.TaskCompleted)));

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync(string manifest)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(root.FullName, "web"));
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "web", "index.html"), "<p>hi</p>", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), manifest, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, ".gitignore"), ".lockflight/\n", cancellationToken);
        await GitAsync(["init", "-q"]);
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);
    }

    private string Payload(string sessionId) => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["hook_event_name"] = "TaskCompleted",
        ["cwd"] = root.FullName,
        ["session_id"] = sessionId,
        ["task_id"] = "task-001",
        ["task_subject"] = "Add login",
    });

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }
}
