using System.Globalization;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Mascot;

/// <summary>
/// <c>lockflight statusline</c>: Claude Code's status line command. Reads the session JSON on stdin, and in a Lockflight project
/// prints Levi's face beside the last verdict from the local ledger (a Stop gate, a gate run, a review run or a guard block in the
/// past day), or, while a code typed in the session waits for its second step, the line to type. Approvals, evidence, fixed or dismissed findings, configuration writes and per-unit Stop times are not verdicts and
/// leave the last one shown.
/// The status line is shown to the person only, never to the model. Outside a Lockflight project it prints nothing.
/// </summary>
internal sealed class StatusLineCommand(CliContext context) : ICommand
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(1);

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string directory = ProjectDirectory(await context.Input.ReadToEndAsync(cancellationToken)) ?? context.WorkingDirectory;
        if (ManifestLocator.Discover(directory) is not { Manifest: Manifest manifest })
        {
            return ExitCode.Pass;
        }

        DateTimeOffset now = context.Time.GetUtcNow();
        LedgerRecord? last = LedgerReader.Read(manifest.Root, now - Window).LastOrDefault(IsVerdict);
        (MascotPose pose, string text) = SessionCommands.WaitingLine(manifest, context.Time, context.Random) is string waiting
            ? (MascotPose.OnDuty, waiting)
            : last is null ? (MascotPose.OnDuty, "on duty") : Describe(last, now);
        bool color = context.Environment.Get(EnvironmentVariables.NoColor) is null;
        if (!color)
        {
            await context.Output.WriteLineAsync($"Lockflight · {text}");
            return ExitCode.Pass;
        }

        IReadOnlyList<string> face = MascotArt.RenderFace();
        TextWriter terminal = context.RawOutput ?? context.Output;
        await terminal.WriteLineAsync($"{face[0]}  \u001b[1mLockflight\u001b[22m{MascotArt.Reset}");
        await terminal.WriteLineAsync($"{face[1]}  {MascotArt.Foreground(Accent(pose))}{TerminalText.Clean(text)}{MascotArt.Reset}");
        return ExitCode.Pass;
    }

    /// <summary>The project the session belongs to: <c>workspace.project_dir</c>, else <c>cwd</c>; null when unreadable.</summary>
    private static string? ProjectDirectory(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("workspace", out JsonElement workspace) && workspace.ValueKind == JsonValueKind.Object
                && workspace.TryGetProperty("project_dir", out JsonElement projectDir) && projectDir.ValueKind == JsonValueKind.String)
            {
                return projectDir.GetString();
            }

            return root.TryGetProperty("cwd", out JsonElement cwd) && cwd.ValueKind == JsonValueKind.String ? cwd.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsVerdict(LedgerRecord record) =>
        record.Type is LedgerEventTypeNames.HookBlocked or LedgerEventTypeNames.ReviewCompleted
        || (record.Type == LedgerEventTypeNames.GateCompleted && record.Signature.StartsWith("gate:", StringComparison.Ordinal)
            && !StopTimings.IsTiming(record.Signature));

    private static (MascotPose Pose, string Text) Describe(LedgerRecord record, DateTimeOffset now)
    {
        string age = Age(now - record.At);
        if (record.Type == LedgerEventTypeNames.HookBlocked)
        {
            return record.Signature.StartsWith("stop:", StringComparison.Ordinal)
                ? (MascotPose.Fail, $"stop gate FAIL · {age}")
                : (MascotPose.Blocked, $"guard blocked {Subject(record.Summary)} · {age}");
        }

        if (record.Type == LedgerEventTypeNames.ReviewCompleted)
        {
            return record.Signature switch
            {
                LedgerSignatures.ReviewClean => (MascotPose.Pass, $"review PASS · {age}"),
                LedgerSignatures.ReviewChanges => (MascotPose.Fail, $"review FAIL · {age}"),
                _ => (MascotPose.NotEarned, $"review NOT-EARNED · {age}"),
            };
        }

        string profile = record.Signature.Split(':')[1];
        MascotPose pose = record.Summary switch
        {
            "PASS" or "N/A" => MascotPose.Pass,
            "NOT-EARNED" => MascotPose.NotEarned,
            _ => MascotPose.Fail,
        };
        return (pose, $"{profile} gate {record.Summary} · {age}");
    }

    private static string Subject(string summary) =>
        summary.StartsWith("protected-path:", StringComparison.Ordinal) ? summary["protected-path:".Length..] : summary;

    private static string Age(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}m ago"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}h ago"),
    };

    private static char Accent(MascotPose pose) => pose switch
    {
        MascotPose.Pass => 'G',
        MascotPose.NotEarned => 'A',
        MascotPose.Fail => 'R',
        MascotPose.Blocked => 'R',
        _ => 'C',
    };
}
