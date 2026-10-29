using System.Text.Json.Nodes;
using Lockflight.Cli.Canaries;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// A task marked completed while the stop gate has not passed for the tree must be refused, through
/// <c>lockflight hook task-completed</c> as Claude Code sends it. In a scratch git repository of its own with the stop gate on and a
/// unit the stop profile judges without a build: the harmless twin, the same completion right after a passing
/// <c>gate run --profile stop</c>, must be allowed; then a file changes, so the cached PASS no longer covers the tree, and the
/// completion must be refused.
/// </summary>
internal static class TaskCompletedCanaries
{
    public const string Name = "task completed: a task marked done without a stop-gate PASS for the tree is refused";

    private const string ManifestText = "version: 1\nstop:\n  gate: true\nunits:\n  - path: web\n    kind: none\n";
    private const string PageFile = "web/index.html";

    public static async Task<CanaryReport> RunAsync(TimeProvider time, CancellationToken cancellationToken)
    {
        using CanaryScratch scratch = new(time);
        if (await CreateAsync(scratch, cancellationToken) is string notCreated)
        {
            return new CanaryReport(Name, CanaryResult.Broken, $"{notCreated}; git is needed for this canary");
        }

        CanaryRun gate = await scratch.RunAsAgentAsync(["gate", "run", "--profile", "stop"], string.Empty, cancellationToken);
        CanaryRun twin = await CompleteAsync(scratch, cancellationToken);
        if (gate.ExitCode != ExitCode.Pass || twin.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(Name, CanaryResult.Broken,
                $"a task completed right after a passing stop gate was refused too (gate exit {(int)gate.ExitCode}, hook exit {(int)twin.ExitCode}: {twin.FirstLine}), so a refusal proves nothing");
        }

        await File.WriteAllTextAsync(scratch.PathOf(PageFile), "<p>changed after the gate passed</p>\n", cancellationToken);
        CanaryRun planted = await CompleteAsync(scratch, cancellationToken);
        return planted.ExitCode == ExitCode.HookBlock
            ? new CanaryReport(Name, CanaryResult.Caught, planted.FirstLine)
            : new CanaryReport(Name, CanaryResult.Missed, $"the hook exited {(int)planted.ExitCode} instead of refusing (2)");
    }

    /// <summary>Writes the project and commits it; the reason when git failed, else null.</summary>
    private static async Task<string?> CreateAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scratch.PathOf("web"));
        await File.WriteAllTextAsync(scratch.PathOf(PageFile), "<p>hi</p>\n", cancellationToken);
        await File.WriteAllTextAsync(scratch.PathOf(Manifest.FileName), ManifestText, cancellationToken);
        await File.WriteAllTextAsync(scratch.PathOf(".gitignore"), ".lockflight/\n", cancellationToken);
        return await CanaryGit.CommitAllAsync(scratch.Project, "canary project", initialize: true, cancellationToken);
    }

    private static Task<CanaryRun> CompleteAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string payload = new JsonObject
        {
            ["session_id"] = "lockflight-canary",
            ["cwd"] = scratch.Project,
            ["hook_event_name"] = "TaskCompleted",
            ["task_id"] = "canary-task",
            ["task_subject"] = "Archive orders",
        }.ToJsonString(JsonFiles.Indented);
        return scratch.RunAsAgentAsync(["hook", "task-completed"], payload, cancellationToken);
    }
}
