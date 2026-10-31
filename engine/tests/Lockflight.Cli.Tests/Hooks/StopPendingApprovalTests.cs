using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Evidence.Blocked;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>
/// An agent whose next step needs a person's first approval is not held at Stop: the refusal leaves a blocked report and a wait,
/// and while the tree is unchanged and the approval still missing, Stop ends with one message instead of running the red suite.
/// </summary>
public sealed class StopPendingApprovalTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-stop-pending-");

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public async Task AStartRefusedForUnapprovedTasksLeavesABlockedReport()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("tasks is not approved", error, StringComparison.Ordinal);
        BlockedReading reading = BlockedReportReader.Read(File.ReadAllText(BlockedReportFile.PathIn(root.FullName)));
        Assert.True(reading.Valid, string.Join("; ", reading.Problems));
        BlockedReport report = reading.Report!;
        Assert.Equal(("engine:redgreen-start", BlockedReasonCode.ApprovalMissing, "f1"), (report.Source, report.Code, report.Feature));
    }

    [Fact]
    public async Task StopWhileTheApprovalIsPendingStopsCleanlyWithoutRunningTheGate()
    {
        await CreateProjectAsync();
        await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        using JsonDocument reply = JsonDocument.Parse(output);
        string message = reply.RootElement.GetProperty("systemMessage").GetString()!;
        Assert.Contains("waiting for a person", message, StringComparison.Ordinal);
        Assert.Contains("tasks is not approved", message, StringComparison.Ordinal);
        Assert.False(HasGateRun());
    }

    [Fact]
    public async Task OnceThePersonApprovesStopRunsTheGateAgain()
    {
        await CreateProjectAsync();
        await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);
        new ApprovalStore(Feature).Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.True(HasGateRun());
    }

    [Fact]
    public async Task ABlockedReportForAnotherReasonDoesNotSkipTheGate()
    {
        await CreateProjectAsync();
        BlockedNeed need = BlockedNeed.Of(BlockedWho.Person, BlockedAction.AcceptBaseline, "accept the baseline", humanOnly: true);
        BlockedReport report = BlockedReport.Of(
            DateTimeOffset.UnixEpoch, "engine:baseline", BlockedReasonCode.BaselineNeeded, "a baseline is needed", [need]);
        BlockedReportFile.WriteLocal(root.FullName, report with { Feature = "f1" });

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.True(HasGateRun());
    }

    [Fact]
    public async Task AnEditAfterTheRefusalPutsTheGateBack()
    {
        await CreateProjectAsync();
        await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, "Later.cs"), "class Later { }\n", TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.True(HasGateRun());
    }

    [Fact]
    public async Task AStaleApprovalDoesNotSkipTheGate()
    {
        await CreateProjectAsync();
        new ApprovalStore(Feature).Save(ApprovalPhase.Tasks, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        await File.AppendAllTextAsync(Feature.TasksPath, "- [ ] T002 [US1] (FR-001) Test B\n", TestContext.Current.CancellationToken);
        await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.True(HasGateRun());
    }

    [Fact]
    public async Task AWaitIsIgnoredWhenTheFeatureBeingWorkedOnIsUnclear()
    {
        await CreateProjectAsync();
        await TestConsole.RunAsync(["redgreen", "start", "--feature", "f1"], string.Empty, root.FullName);
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f2"));

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "stop"], StopPayload(), root.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.True(HasGateRun());
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private async Task CreateProjectAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\nstop:\n  gate: true\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, ".gitignore"), ".lockflight/local/\n.lockflight/ledger/\n", cancellationToken);
        Directory.CreateDirectory(Feature.SpecDirectory);
        await File.WriteAllTextAsync(Feature.SpecPath, "- **FR-001**: A\n", cancellationToken);
        await File.WriteAllTextAsync(Feature.TasksPath, "- [ ] T001 [US1] (FR-001) Test A\n", cancellationToken);
        new ApprovalStore(Feature).Save(ApprovalPhase.Spec, "dev@example.com", DateTimeOffset.UnixEpoch, "0.0.0-test");
        await ProcessRunner.RunAsync("git", ["add", "-A"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"], root.FullName, cancellationToken);
    }

    private string StopPayload() => JsonSerializer.Serialize(new Dictionary<string, string>
    {
        ["hook_event_name"] = "Stop",
        ["cwd"] = root.FullName,
        ["session_id"] = "s-1",
    });

    private bool HasGateRun()
    {
        string directory = Path.Combine(root.FullName, LedgerWriter.RelativeDirectory);
        IEnumerable<string> lines = Directory.Exists(directory) ? Directory.GetFiles(directory, "events-*.jsonl").SelectMany(File.ReadAllLines) : [];
        return lines.Any(line => line.Contains("\"gate.completed\"", StringComparison.Ordinal));
    }
}
