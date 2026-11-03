using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class SessionApprovalTests : IDisposable
{
    private const string FeatureId = "001-orders";
    private const string OldStyleCode = "k7m2qx";
    private const string ApproverEmail = "approver@example.com";
    private const string SeenRecord = ".lockflight/local/approval-seen.json";

    private static readonly Dictionary<string, string> AttendedSession = new()
    {
        [EnvironmentVariables.ClaudeCodeEntrypoint] = "cli",
        [EnvironmentVariables.ClaudeCodeSessionAttended] = "1",
        [EnvironmentVariables.ClaudeCode] = "1",
    };

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-approval-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));

    private string SpecDirectory => Path.Combine(root.FullName, "specs", FeatureId);

    private string SpecPath => Path.Combine(SpecDirectory, "spec.md");

    private string SeenRecordPath => Path.Combine(root.FullName, SeenRecord.Replace('/', Path.DirectorySeparatorChar));

    private string RecordPath(string phase) => Path.Combine(root.FullName, ".lockflight", "features", FeatureId, "approvals", $"{phase}.json");

    [Fact]
    public async Task AnotherSlashCommandIsIgnoredWithoutAWord()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec", new Dictionary<string, string>(), commandName: "lockflight:status");

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("sdk-cli", "0")]
    [InlineData("sdk-cli", "1")]
    [InlineData("cli", "0")]
    [InlineData("cli", null)]
    [InlineData(null, "1")]
    public async Task AnApprovalOutsideAnAttendedInteractiveSessionIsRefused(string? entrypoint, string? attended)
    {
        await CreateProjectAsync();
        await EndTurnAsync();
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeCode] = "1" };
        if (entrypoint is not null)
        {
            environment[EnvironmentVariables.ClaudeCodeEntrypoint] = entrypoint;
        }

        if (attended is not null)
        {
            environment[EnvironmentVariables.ClaudeCodeSessionAttended] = attended;
        }

        (ExitCode exitCode, string output, string error) = await TypeAsync($"spec --feature {FeatureId}", environment);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.Contains($"Or open a separate terminal window ({HumanOnlyCommands.NotWithBang}) and run `cd ", error, StringComparison.Ordinal);
        Assert.Contains($"approve spec --feature {FeatureId}`.", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheApprovalCommandFromAnotherSourceIsRefused()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec", AttendedSession, source: "projectSettings");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("not from the Lockflight plugin", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task ASpecUnchangedSinceTheAgentsTurnEndedIsApprovedInOneStep()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Empty(error);
        using JsonDocument hookOutput = JsonDocument.Parse(output);
        string message = hookOutput.RootElement.GetProperty("systemMessage").GetString()!;
        Assert.StartsWith($"Approved: the spec of {FeatureId}.\n", message, StringComparison.Ordinal);
        Assert.Contains($"spec.md    hash {ArtifactHash.Of(SpecPath)[..12]}", message, StringComparison.Ordinal);
        Assert.Contains("Requirements (2): FR-001, FR-002", message, StringComparison.Ordinal);
        Assert.Contains("User stories (1): 1 Place an order", message, StringComparison.Ordinal);
        Assert.Contains("Open [NEEDS CLARIFICATION] markers: none", message, StringComparison.Ordinal);
        JsonElement specific = hookOutput.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("UserPromptExpansion", specific.GetProperty("hookEventName").GetString());
        Assert.Contains($"approve verify --feature {FeatureId} --require spec", specific.GetProperty("additionalContext").GetString(), StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheOneStepRecordIsTheSameRecordAsTheCommandLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.True(exitCode == ExitCode.Pass, error);
        JsonObject sessionRecord = await ReadRecordAsync("spec", cancellationToken);
        Assert.Equal(time.Now, DateTimeOffset.Parse(sessionRecord["at"]!.GetValue<string>(), CultureInfo.InvariantCulture));

        (ExitCode terminalExit, _, _) = await TestConsole.RunAsync(["approve", "spec", "--feature", FeatureId], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, terminalExit);
        JsonObject terminalRecord = await ReadRecordAsync("spec", cancellationToken);
        sessionRecord.Remove("at");
        terminalRecord.Remove("at");
        Assert.True(JsonNode.DeepEquals(sessionRecord, terminalRecord), $"{sessionRecord.ToJsonString()} != {terminalRecord.ToJsonString()}");
        Assert.Equal(ApproverEmail, terminalRecord["approvedBy"]!.GetValue<string>());
    }

    [Fact]
    public async Task ASpecChangedSinceTheAgentsTurnEndedIsShownAndApprovedOnlyWhenTypedAgain()
    {
        await CreateProjectAsync();
        await EndTurnAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("lockflight: not approved yet (not an error): the spec changed since the agent's last turn ended (spec.md).", error, StringComparison.Ordinal);
        Assert.Contains("Requirements (3): FR-001, FR-002, FR-003", error, StringComparison.Ordinal);
        Assert.Contains($"spec.md    hash {ArtifactHash.Of(SpecPath)[..12]}", error, StringComparison.Ordinal);
        Assert.Contains("type /lockflight:approve spec again", error, StringComparison.Ordinal);
        Assert.Contains("The agent does not see this message.", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.Contains("session-approval:changed", Ledger(), StringComparison.Ordinal);
        Assert.DoesNotContain("hook.blocked", Ledger(), StringComparison.Ordinal);

        (ExitCode againExit, _, string againError) = await TypeAsync("spec");

        Assert.True(againExit == ExitCode.Pass, againError);
        Assert.Equal(ArtifactHash.Of(SpecPath), (await ReadRecordAsync("spec", TestContext.Current.CancellationToken))["artifacts"]!["spec.md"]!.GetValue<string>());
    }

    [Fact]
    public async Task WithoutARecordOfTheAgentsTurnTheSpecIsShownFirst()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.StartsWith("lockflight: not approved yet (not an error): there is no record of the spec from the end of the agent's last turn", error, StringComparison.Ordinal);
        Assert.Contains("Requirements (2): FR-001, FR-002", error, StringComparison.Ordinal);
        Assert.Contains("Current approval: none", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));

        (ExitCode againExit, _, string againError) = await TypeAsync("spec");

        Assert.True(againExit == ExitCode.Pass, againError);
    }

    [Fact]
    public async Task ASpecChangedAfterItWasShownIsShownAgainAndNotApproved()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the spec changed since", error, StringComparison.Ordinal);
        Assert.Contains("Requirements (3): FR-001, FR-002, FR-003", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AHeadlessSessionsTurnEndDoesNotCountAsSeen()
    {
        await CreateProjectAsync();
        await EndTurnAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);
        Dictionary<string, string> headless = new()
        {
            [EnvironmentVariables.ClaudeCodeEntrypoint] = "sdk-cli",
            [EnvironmentVariables.ClaudeCodeSessionAttended] = "0",
            [EnvironmentVariables.ClaudeCode] = "1",
        };
        await SessionTyping.EndTurnAsync(root.FullName, time, "session-1", headless);

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the spec changed since the agent's last turn ended (spec.md)", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AnotherSessionsTurnEndDoesNotCountAsSeen()
    {
        await CreateProjectAsync();
        await SessionTyping.EndTurnAsync(root.FullName, time, "session-2", AttendedSession);

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("there is no record of the spec from the end of the agent's last turn", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TasksUnchangedSinceTheTurnEndedStayApprovableAfterTheSpecWasShown()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, cancellationToken);
        await EndTurnAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks.Replace("(FR-001, FR-002)", "(FR-001, FR-002, FR-003)", StringComparison.Ordinal), cancellationToken);
        await EndTurnAsync();
        await File.AppendAllTextAsync(SpecPath, "\nMore context.\n", cancellationToken);
        await TypeAsync("spec");
        await TypeAsync("spec");

        (ExitCode exitCode, _, string error) = await TypeAsync("tasks");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.True(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task ReapprovingAStaleSpecSaysWhichApprovalItReplaces()
    {
        await CreateProjectAsync();
        await ApproveSpecInATerminalAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains("Replaces an approval that went stale (spec.md changed since)", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDigestSaysAnEarlierApprovalIsStale()
    {
        await CreateProjectAsync();
        await ApproveSpecInATerminalAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        (_, _, string error) = await TypeAsync("spec");

        Assert.Contains("Current approval: an earlier approval is stale (spec.md changed since)", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEndOfTurnRecordOfAnotherFeatureApprovesNothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        string other = Path.Combine(root.FullName, "specs", "002-other");
        Directory.CreateDirectory(other);
        await File.WriteAllTextAsync(Path.Combine(other, "spec.md"), ApprovableArtifacts.Spec, cancellationToken);
        Directory.CreateDirectory(Path.Combine(root.FullName, ".specify"));
        await File.WriteAllTextAsync(Path.Combine(root.FullName, ".specify", "feature.json"), $"{{\"feature_directory\": \"specs/{FeatureId}\"}}", cancellationToken);
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec --feature 002-other");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("Check the spec of 002-other", error, StringComparison.Ordinal);
        Assert.Contains("type /lockflight:approve spec --feature 002-other again", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.FullName, ".lockflight", "features", "002-other", "approvals", "spec.json")));
    }

    [Fact]
    public async Task TheTasksAreApprovedInOneStepAndBindThePlan()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, cancellationToken);
        await ApproveSpecInATerminalAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync($"tasks --feature {FeatureId}");

        Assert.True(exitCode == ExitCode.Pass, error);
        string message = SessionTyping.SystemMessage(output);
        Assert.StartsWith($"Approved: the tasks of {FeatureId}.", message, StringComparison.Ordinal);
        Assert.Contains("Tasks (2): 1 test, 0 stub, 1 implementation; 0 already done; 1 user stories", message, StringComparison.Ordinal);
        Assert.Contains("Requirements covered (2): FR-001, FR-002", message, StringComparison.Ordinal);
        Assert.Contains("plan.md", message, StringComparison.Ordinal);
        JsonObject record = await ReadRecordAsync("tasks", cancellationToken);
        Assert.Equal("tasks", record["phase"]!.GetValue<string>());
        Assert.NotNull(record["artifacts"]!["plan.md"]);
        Assert.NotNull(record["artifacts"]!["tasks.md"]);
    }

    [Fact]
    public async Task APlanDeletedSinceTheTurnEndedIsNotApproved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, cancellationToken);
        await ApproveSpecInATerminalAsync();
        await EndTurnAsync();
        File.Delete(Path.Combine(SpecDirectory, "plan.md"));

        (ExitCode exitCode, _, string error) = await TypeAsync("tasks");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the tasks changed since the agent's last turn ended (plan.md)", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task ASessionApprovalIsLedgeredWithItsChannel()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        await TypeAsync("spec");

        string line = Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory)))));
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal("approval.recorded", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("session", document.RootElement.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task ASignatureFileFromAnOlderEngineIsIgnored()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await EndTurnAsync();
        await TypeAsync("spec");
        await File.WriteAllTextAsync(RecordPath("spec") + ".sig", "-----BEGIN SSH SIGNATURE-----\nnot a signature\n", cancellationToken);

        (ExitCode verifyExit, string verifyOutput, _) = await TestConsole.RunAsync(
            ["approve", "verify", "--feature", FeatureId, "--require", "spec"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, verifyExit);
        Assert.Contains($"PASS {FeatureId}", verifyOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("{\"schema\":\"lockflight.approval-seen/v1\",\"feature\":\"001-orders\",\"artifacts\":[\"spec.md\"]}")]
    [InlineData("{\"schema\":\"lockflight.approval-seen/v0\",\"feature\":\"001-orders\",\"artifacts\":{}}")]
    public async Task AnUnreadableEndOfTurnRecordApprovesNothing(string content)
    {
        await CreateProjectAsync();
        await EndTurnAsync();
        await File.WriteAllTextAsync(SeenRecordPath, content, TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("not approved yet", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALinkInPlaceOfTheEndOfTurnRecordIsRefusedAndNotFollowed(bool turnEnded)
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        string target = Path.Combine(root.FullName, "elsewhere.json");
        await File.WriteAllTextAsync(target, "untouched", cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(SeenRecordPath)!);
        File.CreateSymbolicLink(SeenRecordPath, target);
        if (turnEnded)
        {
            await EndTurnAsync();
        }

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("approval-seen.json is a symbolic link", error, StringComparison.Ordinal);
        Assert.Equal("untouched", await File.ReadAllTextAsync(target, cancellationToken));
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task ALocalStateDirectoryThatIsALinkIsRefused()
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        await CreateProjectAsync();
        DirectoryInfo elsewhere = Directory.CreateDirectory(Path.Combine(root.FullName, "elsewhere"));
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight"));
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, ".lockflight", "local"), elsewhere.FullName);

        await EndTurnAsync();
        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains(".lockflight/local is a symbolic link", error, StringComparison.Ordinal);
        Assert.Empty(elsewhere.GetFileSystemInfos());
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("spec --feature ../001-orders")]
    [InlineData("spec --feature specs/001-orders")]
    [InlineData("spec --feature specs\\001-orders")]
    [InlineData("spec --feature ..")]
    [InlineData("spec --feature .")]
    [InlineData("--feature ../001-orders")]
    public async Task AFeatureGivenAsAPathIsRefused(string arguments)
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("--feature takes a feature ID", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("spec --force")]
    [InlineData("spec --sign")]
    [InlineData("spec --feature")]
    [InlineData("spec --key approver-key")]
    [InlineData("spec approved")]
    [InlineData("spec k7m2qx extra")]
    [InlineData("spec --yes")]
    [InlineData("spec --feature 001-orders --feature 001-orders")]
    [InlineData("specs")]
    [InlineData("spec-tasks")]
    public async Task AnUnknownOrMalformedArgumentIsAUsageRefusal(string arguments)
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.Contains("session-approval:usage", Ledger(), StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AnOldStyleCodeApprovesWhenTheOneStepRulesPassAndSaysCodesAreNoLongerNeeded()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync($"spec {OldStyleCode}");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith($"Approved: the spec of {FeatureId} (codes are no longer needed: /lockflight:approve alone approves).", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AnOldStyleCodeForContentNotSeenIsAUsageRefusal()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {OldStyleCode}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.StartsWith("lockflight: nothing was approved: codes are no longer needed; type /lockflight:approve spec", error, StringComparison.Ordinal);
        Assert.Contains("session-approval:usage", Ledger(), StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));

        (ExitCode plainExit, _, string plainError) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, plainExit);
        Assert.Contains("there is no record of the spec", plainError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtraWhitespaceAroundTheWordsIsIgnored()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode recordExit, _, string error) = await TypeAsync("\tSPEC \n   ");

        Assert.True(recordExit == ExitCode.Pass, error);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("Lockflight:Approve")]
    [InlineData("LOCKFLIGHT:APPROVE")]
    public async Task TheCommandInAnotherCaseIsStillCheckedByTheEngine(string commandName)
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec", new Dictionary<string, string>(), commandName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("Plugin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnySourceButThePluginIsRefused(string? source)
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec", AttendedSession, source: source);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("not from the Lockflight plugin", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AnEngineErrorDuringTheApprovalRefusesInsteadOfPassingTheCommandOn()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Uses Unix file permissions.");
            return;
        }

        await CreateProjectAsync();
        File.SetUnixFileMode(SpecPath, UnixFileMode.None);
        try
        {
            (ExitCode exitCode, string output, string error) = await TypeAsync("spec");

            Assert.Equal(ExitCode.HookBlock, exitCode);
            Assert.Empty(output);
            Assert.Contains("nothing was approved: internal error", error, StringComparison.Ordinal);
            Assert.False(File.Exists(RecordPath("spec")));
        }
        finally
        {
            File.SetUnixFileMode(SpecPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task AnEngineErrorBeforeTheApprovalStepBlocksInsteadOfPassingTheCommandOn()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec", new FailingEnvironment(AttendedSession));

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEngineErrorForAnotherSlashCommandDoesNotBlockIt()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, _) = await TypeAsync("x", new FailingEnvironment(AttendedSession), commandName: "lockflight:status");

        Assert.Equal(ExitCode.Pass, exitCode);
    }

    [Fact]
    public async Task TheCommandAloneApprovesASpecNotYetApproved()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync(string.Empty);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith($"Approved: the spec of {FeatureId}.", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheCommandAloneApprovesAStaleSpecUnchangedSinceTheTurnEnded()
    {
        await CreateProjectAsync();
        await ApproveSpecInATerminalAsync();
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Equal(ArtifactHash.Of(SpecPath), (await ReadRecordAsync("spec", TestContext.Current.CancellationToken))["artifacts"]!["spec.md"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheCommandAloneApprovesTheTasksOnceTheSpecIsApproved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, cancellationToken);
        await ApproveSpecInATerminalAsync();
        await EndTurnAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync(string.Empty);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith($"Approved: the tasks of {FeatureId}.", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task TheFeatureOptionAloneChoosesThePhaseOfThatFeature()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync($"--feature {FeatureId}");

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheCommandAloneWithNothingWaitingListsThePhasesAndIsNotLedgeredAsABlock()
    {
        await CreateProjectAsync();
        await ApproveSpecInATerminalAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.StartsWith(
            $"lockflight: nothing was approved: no approval is waiting for {FeatureId} (spec: approved by {ApproverEmail}; tasks: tasks.md not written yet); "
            + "to approve one anyway, type /lockflight:approve spec or /lockflight:approve tasks",
            error,
            StringComparison.Ordinal);
        Assert.Contains("\"session-command.answered\"", Ledger(), StringComparison.Ordinal);
        Assert.DoesNotContain("hook.blocked", Ledger(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCommandAloneWithoutAFeatureSaysHowToNameOne()
    {
        await CreateProjectAsync();
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "002-other"));

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("type /lockflight:approve spec or /lockflight:approve tasks with --feature <id>", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnattendedApprovalIsLedgeredAsABlock()
    {
        await CreateProjectAsync();

        await TypeAsync("spec", new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        Assert.Contains("session-approval:unattended", Ledger(), StringComparison.Ordinal);
        Assert.Contains("hook.blocked", Ledger(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpecThatFailsItsPhaseCheckIsRefusedAndNothingIsRecorded()
    {
        await CreateProjectAsync();
        await File.WriteAllTextAsync(
            SpecPath, "# Orders\n\n### User Story 1 - Place an order\n\n- **FR-001**: Customers place orders\n", TestContext.Current.CancellationToken);
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.StartsWith($"lockflight: nothing was approved: the spec of {FeatureId} does not pass the phase check an approval needs (specify):", error, StringComparison.Ordinal);
        Assert.Contains("  FAIL spec.stories: no Given/When/Then scenario in: User Story 1\n", error, StringComparison.Ordinal);
        Assert.Contains($"phase check specify --feature {FeatureId}` until it passes, and approve again.", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.Contains("session-approval:phase-check", Ledger(), StringComparison.Ordinal);
        Assert.DoesNotContain("hook.blocked", Ledger(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TasksAreRefusedBeforeTheSpecIsApproved()
    {
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), ApprovableArtifacts.Tasks, TestContext.Current.CancellationToken);
        await EndTurnAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("tasks");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("does not pass the phase check an approval needs (tasks):", error, StringComparison.Ordinal);
        Assert.Contains("  FAIL spec.approved: spec is not approved", error, StringComparison.Ordinal);
        Assert.Contains($"phase check tasks --feature {FeatureId}`", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));
    }

    [Fact]
    public async Task TheApprovalStoresNoSessionCode()
    {
        await CreateProjectAsync();
        await EndTurnAsync();

        await TypeAsync("spec");
        await TypeAsync(string.Empty);

        Assert.False(Directory.Exists(Path.Combine(root.FullName, SessionCodes.RelativeDirectory)));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    /// <summary>An environment whose every read past the session markers fails, as an engine fault outside the approval would.</summary>
    private sealed class FailingEnvironment(IReadOnlyDictionary<string, string> session) : IReadOnlyDictionary<string, string>
    {
        public int Count => session.Count;

        public IEnumerable<string> Keys => session.Keys;

        public IEnumerable<string> Values => session.Values;

        public string this[string key] => TryGetValue(key, out string? value) ? value : throw new KeyNotFoundException(key);

        public bool ContainsKey(string key) => TryGetValue(key, out _);

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) =>
            session.TryGetValue(key, out value) ? true : throw new InvalidOperationException($"environment unavailable ({key})");

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => session.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private async Task CreateProjectAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.email", ApproverEmail], root.FullName, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n", cancellationToken);
        Directory.CreateDirectory(SpecDirectory);
        await File.WriteAllTextAsync(SpecPath, ApprovableArtifacts.Spec, cancellationToken);
    }

    private async Task ApproveSpecInATerminalAsync()
    {
        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["approve", "spec", "--feature", FeatureId], string.Empty, root.FullName);
        Assert.True(exitCode == ExitCode.Pass, error);
    }

    /// <summary>The agent's turn ends: Claude Code runs the Stop hook.</summary>
    private Task<string> EndTurnAsync() => SessionTyping.EndTurnAsync(root.FullName, time);

    private string Ledger()
    {
        string directory = Path.Combine(root.FullName, ".lockflight", "ledger");
        return Directory.Exists(directory) ? string.Concat(Directory.GetFiles(directory).Select(File.ReadAllText)) : string.Empty;
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(string arguments) => TypeAsync(arguments, AttendedSession);

    private async Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(
        string arguments, IReadOnlyDictionary<string, string> environment, string commandName = SessionApproval.CommandName, string? source = "plugin")
    {
        Dictionary<string, string> fields = new()
        {
            ["session_id"] = "session-1",
            ["cwd"] = root.FullName,
            ["hook_event_name"] = "UserPromptExpansion",
            ["expansion_type"] = "slash_command",
            ["command_name"] = commandName,
            ["command_args"] = arguments,
            ["prompt"] = $"/{commandName} {arguments}",
        };
        if (source is not null)
        {
            fields["command_source"] = source;
        }

        string payload = JsonSerializer.Serialize(fields);
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(payload), output, error, root.FullName, time)
        {
            Environment = EnvironmentReader.Of(environment),
        };

        ExitCode exitCode = await new HookCommand(context).RunAsync(HookEvent.UserPromptExpansion, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private async Task<JsonObject> ReadRecordAsync(string phase, CancellationToken cancellationToken) =>
        JsonNode.Parse(await File.ReadAllTextAsync(RecordPath(phase), cancellationToken))!.AsObject();
}
