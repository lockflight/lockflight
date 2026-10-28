using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// <c>/lockflight:approve</c> typed in a session, sent through <c>lockflight hook user-prompt-expansion</c> as Claude Code sends it.
/// Refused: the command in an unattended session, the command from another source than the plugin, and a second step with a wrong,
/// a used or an expired code. The harmless twin, the person's own two steps in an attended session with the code shown, must show
/// the digest and then record the approval, or the refusals prove nothing. Every command shares these checks
/// (<see cref="SessionCommandGate"/>); approve needs no build, so it stands for all four.
/// </summary>
internal static class SessionCommandCanaries
{
    private const string UserPromptExpansion = "user-prompt-expansion";
    private const string PluginSource = "plugin";
    private const string OtherSource = "projectSettings";
    private const string FeatureId = "001-canary";
    private const string Slot = "spec";
    private const string StepOne = "Step 1 of 2";
    private const string CodeLinePrefix = "Type within";
    private const string WrongCode = "zzzzzz";
    private const string OtherWrongCode = "yyyyyy";

    private const string HeadlessName = "session command: /lockflight:approve in a headless session is refused";
    private const string OtherSourceName = "session command: /lockflight:approve from another source than the plugin is refused";
    private const string WrongName = "session command: a wrong code is refused";
    private const string UsedName = "session command: a used code is refused";
    private const string ExpiredName = "session command: an expired code is refused";

    private static readonly Dictionary<string, string> AttendedSession = new(StringComparer.Ordinal)
    {
        [EnvironmentVariables.ClaudeCode] = "1",
        [EnvironmentVariables.ClaudeCodeEntrypoint] = "cli",
        [EnvironmentVariables.ClaudeCodeSessionAttended] = "1",
    };

    private static readonly Dictionary<string, string> HeadlessSession = new(StringComparer.Ordinal)
    {
        [EnvironmentVariables.ClaudeCode] = "1",
        [EnvironmentVariables.ClaudeCodeEntrypoint] = "sdk-cli",
        [EnvironmentVariables.ClaudeCodeSessionAttended] = "0",
    };

    /// <summary>Past a code's lifetime, beyond the minute a clock may step back.</summary>
    private static readonly TimeSpan PastLifetime = SessionCodes.Lifetime + TimeSpan.FromMinutes(2);

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string specDirectory = scratch.PathOf($"specs/{FeatureId}");
        Directory.CreateDirectory(specDirectory);
        await File.WriteAllTextAsync(Path.Combine(specDirectory, "spec.md"), "# Canary\n\n- **FR-001**: Orders are archived\n", cancellationToken);

        List<CanaryReport> reports =
        [
            await NoCodeIssuedAsync(scratch, HeadlessName, HeadlessSession, PluginSource, cancellationToken),
            await NoCodeIssuedAsync(scratch, OtherSourceName, AttendedSession, OtherSource, cancellationToken),
        ];

        // A wrong code is typed against a code of its own, so a wrong code that is accepted is reported as missed, not as a broken twin.
        if (await ShowAsync(scratch, cancellationToken) is not string firstCode)
        {
            return Broken("the person's own /lockflight:approve showed no code, so a refusal proves nothing");
        }

        CanaryRun wrong = await TypeAsync(scratch, $"{Slot} {(firstCode == WrongCode ? OtherWrongCode : WrongCode)}", AttendedSession, PluginSource, scratch.Time, cancellationToken);
        if (await ShowAsync(scratch, cancellationToken) is not string code)
        {
            return Broken("the person's own /lockflight:approve showed no code the second time, so a refusal proves nothing");
        }

        CanaryRun recorded = await TypeAsync(scratch, $"{Slot} {code}", AttendedSession, PluginSource, scratch.Time, cancellationToken);
        if (recorded.ExitCode != ExitCode.Pass)
        {
            return Broken($"the code shown was refused too (exit {(int)recorded.ExitCode}: {recorded.FirstLine}), so a refusal proves nothing");
        }

        reports.Add(Refused(WrongName, wrong));
        reports.Add(Refused(UsedName, await TypeAsync(scratch, $"{Slot} {code}", AttendedSession, PluginSource, scratch.Time, cancellationToken)));
        if (await ShowAsync(scratch, cancellationToken) is not string laterCode)
        {
            reports.Add(new CanaryReport(ExpiredName, CanaryResult.Broken, "step 1 showed no code the third time, so a refusal proves nothing"));
            return reports;
        }

        ShiftedTime later = new(scratch.Time, PastLifetime);
        reports.Add(Refused(ExpiredName, await TypeAsync(scratch, $"{Slot} {laterCode}", AttendedSession, PluginSource, later, cancellationToken)));
        return reports;
    }

    /// <summary>A first step the gate must refuse before it issues a code: no digest shown, no code waiting.</summary>
    private static async Task<CanaryReport> NoCodeIssuedAsync(
        CanaryScratch scratch, string name, IReadOnlyDictionary<string, string> session, string source, CancellationToken cancellationToken)
    {
        string waiting = scratch.PathOf($"{SessionCodes.RelativeDirectory}/{Slot}.json");
        CanaryRun run = await TypeAsync(scratch, Slot, session, source, scratch.Time, cancellationToken);
        bool issued = File.Exists(waiting) || run.Error.StartsWith(StepOne, StringComparison.Ordinal);
        if (File.Exists(waiting))
        {
            File.Delete(waiting);
        }

        return run.ExitCode == ExitCode.HookBlock && !issued
            ? new CanaryReport(name, CanaryResult.Caught, run.FirstLine)
            : new CanaryReport(name, CanaryResult.Missed, issued ? "it showed the digest and issued a code" : $"the hook exited {(int)run.ExitCode} instead of refusing (2)");
    }

    private static CanaryReport Refused(string name, CanaryRun run) => run.ExitCode == ExitCode.HookBlock
        ? new CanaryReport(name, CanaryResult.Caught, run.FirstLine)
        : new CanaryReport(name, CanaryResult.Missed, $"the approval was recorded (exit {(int)run.ExitCode})");

    private static CanaryReport[] Broken(string detail) =>
        [.. new[] { HeadlessName, OtherSourceName, WrongName, UsedName, ExpiredName }.Select(name => new CanaryReport(name, CanaryResult.Broken, detail))];

    /// <summary>The person's first step: the one-time code on the digest's line to type; null when no digest was shown.</summary>
    private static async Task<string?> ShowAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        CanaryRun run = await TypeAsync(scratch, Slot, AttendedSession, PluginSource, scratch.Time, cancellationToken);
        return run.ExitCode == ExitCode.HookBlock && run.Error.StartsWith(StepOne, StringComparison.Ordinal)
            ? run.Error.Split('\n').FirstOrDefault(line => line.StartsWith(CodeLinePrefix, StringComparison.Ordinal))?.Split(' ')[^1].Trim()
            : null;
    }

    private static Task<CanaryRun> TypeAsync(
        CanaryScratch scratch, string arguments, IReadOnlyDictionary<string, string> session, string source, TimeProvider clock, CancellationToken cancellationToken)
    {
        string payload = new JsonObject
        {
            ["session_id"] = "lockflight-canary",
            ["cwd"] = scratch.Project,
            ["hook_event_name"] = "UserPromptExpansion",
            ["expansion_type"] = "slash_command",
            ["command_name"] = SessionApproval.CommandName,
            ["command_args"] = arguments,
            ["command_source"] = source,
            ["prompt"] = $"/{SessionApproval.CommandName} {arguments}",
        }.ToJsonString(JsonFiles.Indented);
        return scratch.RunInSessionAsync(["hook", UserPromptExpansion], payload, session, clock, cancellationToken);
    }
}
