using System.Text.Json.Nodes;
using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// The in-session human commands typed in a session, sent through <c>lockflight hook user-prompt-expansion</c> as Claude Code sends
/// them. Refused: <c>/lockflight:approve</c> in an unattended session, from another source than the plugin, and for a spec changed
/// since the agent's turn ended; and a second step of <c>/lockflight:reset</c> (the commands that still take a code) with a wrong, a
/// used or an expired code. The harmless twins, the person's own approval of a spec unchanged since the turn ended and their own two
/// reset steps with the code shown, must succeed, or the refusals prove nothing. Every command shares the source and session checks
/// (<see cref="SessionCommandGate"/>), and every code command the code checks, so these two stand for all four.
/// </summary>
internal static class SessionCommandCanaries
{
    private const string UserPromptExpansion = "user-prompt-expansion";
    private const string PluginSource = "plugin";
    private const string OtherSource = "projectSettings";
    private const string FeatureId = "001-canary";
    private const string StepOne = "Step 1 of 2";
    private const string CodeLinePrefix = "Your code: ";
    private const string WrongCode = "zzzzzz";
    private const string OtherWrongCode = "yyyyyy";
    private const string Spec = "# Canary\n\n### User Story 1 - Archive orders\n\n1. **Given** an old order, **When** a day passes, **Then** it is archived\n\n- **FR-001**: Orders are archived\n";

    private const string HeadlessName = "session command: /lockflight:approve in a headless session is refused";
    private const string OtherSourceName = "session command: /lockflight:approve from another source than the plugin is refused";
    private const string ChangedName = "session command: /lockflight:approve of a spec changed since the agent's turn ended is refused";
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
        List<CanaryReport> reports = [.. await ApprovalAsync(scratch, cancellationToken)];
        reports.AddRange(await CodesAsync(scratch, cancellationToken));
        return reports;
    }

    /// <summary>The approval's refusals, then its twin: the same command for the spec as the turn left it records the approval.</summary>
    private static async Task<IReadOnlyList<CanaryReport>> ApprovalAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string specDirectory = scratch.PathOf($"specs/{FeatureId}");
        string specPath = Path.Combine(specDirectory, "spec.md");
        Directory.CreateDirectory(specDirectory);
        await File.WriteAllTextAsync(specPath, Spec, cancellationToken);
        await EndTurnAsync(scratch, cancellationToken);

        List<CanaryReport> reports =
        [
            NothingApproved(scratch, HeadlessName, await TypeAsync(scratch, SessionApproval.CommandName, "spec", HeadlessSession, PluginSource, scratch.Time, cancellationToken)),
            NothingApproved(scratch, OtherSourceName, await TypeAsync(scratch, SessionApproval.CommandName, "spec", AttendedSession, OtherSource, scratch.Time, cancellationToken)),
        ];

        await File.AppendAllTextAsync(specPath, "- **FR-002**: Archived orders stay readable\n", cancellationToken);
        reports.Add(NothingApproved(scratch, ChangedName, await TypeAsync(scratch, SessionApproval.CommandName, "spec", AttendedSession, PluginSource, scratch.Time, cancellationToken)));

        await EndTurnAsync(scratch, cancellationToken);
        CanaryRun twin = await TypeAsync(scratch, SessionApproval.CommandName, "spec", AttendedSession, PluginSource, scratch.Time, cancellationToken);
        if (twin.ExitCode != ExitCode.Pass || !File.Exists(ApprovalPath(scratch)))
        {
            return Broken(
                [HeadlessName, OtherSourceName, ChangedName],
                $"the person's own /lockflight:approve of the spec as the turn left it was refused too (exit {(int)twin.ExitCode}: {twin.FirstLine}), so a refusal proves nothing");
        }

        return reports;
    }

    /// <summary>The code checks every two-step command shares, on <c>/lockflight:reset</c>, which needs no build.</summary>
    private static async Task<IReadOnlyList<CanaryReport>> CodesAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string[] names = [WrongName, UsedName, ExpiredName];
        SaveRedGreenRecord(scratch);

        // A wrong code is typed against a code of its own, so a wrong code that is accepted is reported as missed, not as a broken twin.
        if (await ShowResetAsync(scratch, cancellationToken) is not string firstCode)
        {
            return Broken(names, "the person's own /lockflight:reset showed no code, so a refusal proves nothing");
        }

        CanaryRun wrong = await TypeAsync(scratch, SessionReset.CommandName, firstCode == WrongCode ? OtherWrongCode : WrongCode, AttendedSession, PluginSource, scratch.Time, cancellationToken);
        if (await ShowResetAsync(scratch, cancellationToken) is not string code)
        {
            return Broken(names, "the person's own /lockflight:reset showed no code the second time, so a refusal proves nothing");
        }

        CanaryRun used = await TypeAsync(scratch, SessionReset.CommandName, code, AttendedSession, PluginSource, scratch.Time, cancellationToken);
        if (used.ExitCode != ExitCode.Pass)
        {
            return Broken(names, $"the code shown was refused too (exit {(int)used.ExitCode}: {used.FirstLine}), so a refusal proves nothing");
        }

        List<CanaryReport> reports =
        [
            Refused(WrongName, wrong),
            Refused(UsedName, await TypeAsync(scratch, SessionReset.CommandName, code, AttendedSession, PluginSource, scratch.Time, cancellationToken)),
        ];
        SaveRedGreenRecord(scratch);
        if (await ShowResetAsync(scratch, cancellationToken) is not string laterCode)
        {
            reports.Add(new CanaryReport(ExpiredName, CanaryResult.Broken, "step 1 showed no code the third time, so a refusal proves nothing"));
            return reports;
        }

        ShiftedTime later = new(scratch.Time, PastLifetime);
        reports.Add(Refused(ExpiredName, await TypeAsync(scratch, SessionReset.CommandName, laterCode, AttendedSession, PluginSource, later, cancellationToken)));
        return reports;
    }

    private static string ApprovalPath(CanaryScratch scratch) => scratch.PathOf($".lockflight/features/{FeatureId}/approvals/spec.json");

    /// <summary>An approval the hook must refuse: exit 2 and no approval record.</summary>
    private static CanaryReport NothingApproved(CanaryScratch scratch, string name, CanaryRun run)
    {
        bool recorded = File.Exists(ApprovalPath(scratch));
        if (recorded)
        {
            File.Delete(ApprovalPath(scratch));
        }

        return run.ExitCode == ExitCode.HookBlock && !recorded
            ? new CanaryReport(name, CanaryResult.Caught, run.FirstLine)
            : new CanaryReport(name, CanaryResult.Missed, recorded ? "the approval was recorded" : $"the hook exited {(int)run.ExitCode} instead of refusing (2)");
    }

    private static CanaryReport Refused(string name, CanaryRun run) => run.ExitCode == ExitCode.HookBlock
        ? new CanaryReport(name, CanaryResult.Caught, run.FirstLine)
        : new CanaryReport(name, CanaryResult.Missed, $"the evidence was discarded (exit {(int)run.ExitCode})");

    private static CanaryReport[] Broken(IEnumerable<string> names, string detail) =>
        [.. names.Select(name => new CanaryReport(name, CanaryResult.Broken, detail))];

    private static void SaveRedGreenRecord(CanaryScratch scratch)
    {
        Feature feature = new(FeatureId, scratch.PathOf($"specs/{FeatureId}"), scratch.PathOf($".lockflight/features/{FeatureId}"));
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Canary.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, [])]));
        record.Save(RedGreenRecord.PathFor(feature), feature.Id);
    }

    /// <summary>The person's first reset step: the one-time code on the digest's line to type; null when no digest was shown.</summary>
    private static async Task<string?> ShowResetAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        CanaryRun run = await TypeAsync(scratch, SessionReset.CommandName, string.Empty, AttendedSession, PluginSource, scratch.Time, cancellationToken);
        return run.ExitCode == ExitCode.HookBlock && run.Error.StartsWith(StepOne, StringComparison.Ordinal)
            ? run.Error.Split('\n').FirstOrDefault(line => line.StartsWith(CodeLinePrefix, StringComparison.Ordinal))?[CodeLinePrefix.Length..].Split(' ')[0]
            : null;
    }

    /// <summary>The agent's turn ends: the Stop hook records the spec as the person can now see it.</summary>
    private static Task<CanaryRun> EndTurnAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        string payload = new JsonObject
        {
            ["session_id"] = "lockflight-canary",
            ["cwd"] = scratch.Project,
            ["hook_event_name"] = "Stop",
        }.ToJsonString(JsonFiles.Indented);
        return scratch.RunInSessionAsync(["hook", "stop"], payload, AttendedSession, scratch.Time, cancellationToken);
    }

    private static Task<CanaryRun> TypeAsync(
        CanaryScratch scratch, string command, string arguments, IReadOnlyDictionary<string, string> session, string source, TimeProvider clock, CancellationToken cancellationToken)
    {
        string payload = new JsonObject
        {
            ["session_id"] = "lockflight-canary",
            ["cwd"] = scratch.Project,
            ["hook_event_name"] = "UserPromptExpansion",
            ["expansion_type"] = "slash_command",
            ["command_name"] = command,
            ["command_args"] = arguments,
            ["command_source"] = source,
            ["prompt"] = $"/{command} {arguments}",
        }.ToJsonString(JsonFiles.Indented);
        return scratch.RunInSessionAsync(["hook", UserPromptExpansion], payload, session, clock, cancellationToken);
    }
}
