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
    private const string Code = "k7m2qx";
    private const string SecondCode = "p3w9ha";
    private const string ApproverEmail = "approver@example.com";

    private static readonly Dictionary<string, string> AttendedSession = new()
    {
        [EnvironmentVariables.ClaudeCodeEntrypoint] = "cli",
        [EnvironmentVariables.ClaudeCodeSessionAttended] = "1",
        [EnvironmentVariables.ClaudeCode] = "1",
    };

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-approval-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly RandomSource codes = RandomSource.WithCodes(Code, SecondCode);

    private string SpecDirectory => Path.Combine(root.FullName, "specs", FeatureId);

    private string SpecPath => Path.Combine(SpecDirectory, "spec.md");

    private string RecordPath(string phase) => Path.Combine(root.FullName, ".lockflight", "features", FeatureId, "approvals", $"{phase}.json");

    private string WaitingCodePath(string phase) => Path.Combine(root.FullName, SessionCodes.RelativeDirectory, $"{phase}.json");

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
        Dictionary<string, string> environment = new() { [EnvironmentVariables.ClaudeCode] = "1" };
        if (entrypoint is not null)
        {
            environment[EnvironmentVariables.ClaudeCodeEntrypoint] = entrypoint;
        }

        if (attended is not null)
        {
            environment[EnvironmentVariables.ClaudeCodeSessionAttended] = attended;
        }

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec --sign", environment);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.Contains(HumanOnlyCommands.WhereToRun, error, StringComparison.Ordinal);
        Assert.Contains("approve spec --sign", error, StringComparison.Ordinal);
        Assert.False(File.Exists(WaitingCodePath("spec")));
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheApprovalCommandFromAnotherSourceIsRefused()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync("spec", AttendedSession, source: "projectSettings");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("not from the Lockflight plugin", error, StringComparison.Ordinal);
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task TheFirstStepShowsTheDigestAndStoresOnlyAHashOfTheCode()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("Step 1 of 2 — nothing has been approved yet. Check the spec of 001-orders you are approving", error, StringComparison.Ordinal);
        Assert.EndsWith(SessionCommandGate.NotAnError, error, StringComparison.Ordinal);
        Assert.Contains($"spec.md    {ArtifactHash.Of(SpecPath)[..12]}", error, StringComparison.Ordinal);
        Assert.Contains("Requirements (2): FR-001, FR-002", error, StringComparison.Ordinal);
        Assert.Contains("User stories (1): 1 Place an order", error, StringComparison.Ordinal);
        Assert.Contains("Open [NEEDS CLARIFICATION] markers: spec.md line 6", error, StringComparison.Ordinal);
        Assert.Contains("Current approval: none", error, StringComparison.Ordinal);
        Assert.Contains($"Type within 10 minutes: /lockflight:approve spec {Code}\n", error, StringComparison.Ordinal);
        string waiting = await File.ReadAllTextAsync(WaitingCodePath("spec"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(Code, waiting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ArtifactHash.Of(SpecPath), waiting, StringComparison.Ordinal);
        Assert.Contains(FeatureId, waiting, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheDigestSaysAnEarlierApprovalIsStale()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await TypeAsync($"spec {Code}");
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        (_, _, string error) = await TypeAsync("spec");

        Assert.Contains("Current approval: an earlier approval is stale (spec.md changed since)", error, StringComparison.Ordinal);
        Assert.Contains($"/lockflight:approve spec {SecondCode}", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecondStepRecordsTheSameRecordAsTheCommandLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode exitCode, string output, string error) = await TypeAsync($"spec {Code.ToUpperInvariant()}");

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Empty(error);
        using JsonDocument hookOutput = JsonDocument.Parse(output);
        Assert.Equal($"Step 2 of 2 — approved: the spec of {FeatureId} (spec.md {ArtifactHash.Of(SpecPath)[..12]}).", hookOutput.RootElement.GetProperty("systemMessage").GetString());
        JsonElement specific = hookOutput.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("UserPromptExpansion", specific.GetProperty("hookEventName").GetString());
        Assert.Contains($"approve verify --feature {FeatureId} --require spec", specific.GetProperty("additionalContext").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(WaitingCodePath("spec")));
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
    public async Task AWrongCodeIsRefusedAndTheShownCodeStillWorks()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode wrongExit, _, string error) = await TypeAsync("spec aaaaaa");

        Assert.Equal(ExitCode.HookBlock, wrongExit);
        Assert.Contains("not the code shown", error, StringComparison.Ordinal);
        Assert.DoesNotContain("aaaaaa", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));

        (ExitCode rightExit, _, _) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.Pass, rightExit);
    }

    [Fact]
    public async Task AnExpiredCodeIsRefused()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        time.Advance(SessionCodes.Lifetime);

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the code expired", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task AUsedCodeDoesNotApproveAgain()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await TypeAsync($"spec {Code}");
        File.Delete(RecordPath("spec"));

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("no approval code is waiting", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task ASpecChangedBetweenTheStepsIsNotApproved()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await File.AppendAllTextAsync(SpecPath, "- **FR-003**: Refunds\n", TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the spec changed since you saw it (spec.md); run /lockflight:approve spec again", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task ACodeShownForOneFeatureDoesNotApproveAnother()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code} --feature 002-other");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains($"shown for feature {FeatureId}, not 002-other", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task TheTasksDigestCountsTasksAndTheApprovalBindsThePlan()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(SpecDirectory, "tasks.md"),
            "### Tests for User Story 1\n- [ ] T001 [US1] (FR-001) Test placing an order\n\n### Implementation\n- [ ] T002 [US1] (FR-001, FR-002) Place the order\n",
            cancellationToken);

        (ExitCode showExit, _, string digest) = await TypeAsync($"tasks --feature {FeatureId}");

        Assert.Equal(ExitCode.HookBlock, showExit);
        Assert.Contains("Tasks (2): 1 test, 0 stub, 1 implementation; 0 already done; 1 user stories", digest, StringComparison.Ordinal);
        Assert.Contains("Requirements covered (2): FR-001, FR-002", digest, StringComparison.Ordinal);
        Assert.Contains("plan.md", digest, StringComparison.Ordinal);
        Assert.Contains($"/lockflight:approve tasks {Code}", digest, StringComparison.Ordinal);

        (ExitCode recordExit, _, _) = await TypeAsync($"tasks {Code}");

        Assert.Equal(ExitCode.Pass, recordExit);
        JsonObject record = await ReadRecordAsync("tasks", cancellationToken);
        Assert.Equal("tasks", record["phase"]!.GetValue<string>());
        Assert.NotNull(record["artifacts"]!["plan.md"]);
        Assert.NotNull(record["artifacts"]!["tasks.md"]);
    }

    [Fact]
    public async Task ASessionApprovalIsLedgeredWithItsChannel()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        await TypeAsync($"spec {Code}");

        string line = Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(Path.Combine(root.FullName, LedgerWriter.RelativeDirectory)))));
        using JsonDocument document = JsonDocument.Parse(line);
        Assert.Equal("approval.recorded", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("session", document.RootElement.GetProperty("channel").GetString());
        Assert.DoesNotContain(Code, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASigningFailureRecordsNothingAndKeepsTheCode()
    {
        await CreateProjectAsync();
        await TypeAsync("spec --sign");

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code} --sign --key {Path.Combine(root.FullName, "missing-key")}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("signing failed, so nothing was recorded", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(RecordPath("spec") + ".sig"));
        Assert.True(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task ASignedSessionApprovalVerifiesAgainstTheAllowedSigners()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        string key = Path.Combine(root.FullName, "approver-key");
        await ProcessRunner.RunAsync("ssh-keygen", ["-q", "-t", "ed25519", "-N", string.Empty, "-C", ApproverEmail, "-f", key], root.FullName, cancellationToken);
        string publicKey = (await File.ReadAllTextAsync(key + ".pub", cancellationToken)).Trim();
        string signers = Path.Combine(root.FullName, "allowed_signers");
        await File.WriteAllTextAsync(signers, $"{ApproverEmail} namespaces=\"{ApprovalStore.SignatureNamespace}\" {publicKey}\n", cancellationToken);
        await TypeAsync("spec --sign");

        (ExitCode exitCode, string output, _) = await TypeAsync($"spec {Code} --sign --key {key}");

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains(", signed.", output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RecordPath("spec"))!, "*.unsigned*"));
        (ExitCode verifyExit, string verifyOutput, _) = await TestConsole.RunAsync(
            ["approve", "verify", "--feature", FeatureId, "--require", "spec", "--allowed-signers", signers], string.Empty, root.FullName);
        Assert.Equal(ExitCode.Pass, verifyExit);
        Assert.Contains("signed by", verifyOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("")]
    public async Task AnUnreadableWaitingCodeApprovesNothing(string content)
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await File.WriteAllTextAsync(WaitingCodePath("spec"), content, TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("no approval code is waiting", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Theory]
    [InlineData("schema", "\"lockflight.session-code/v0\"")]
    [InlineData("command", "\"tasks\"")]
    [InlineData("subject", "\"../001-orders\"")]
    [InlineData("subject", "\"specs/001-orders\"")]
    [InlineData("subject", "\"..\"")]
    [InlineData("subject", "42")]
    [InlineData("payload", "[1]")]
    [InlineData("salt", "\"not hex\"")]
    [InlineData("codeHash", "null")]
    [InlineData("expiresAt", "\"tomorrow\"")]
    [InlineData("bound", "[\"spec.md\"]")]
    public async Task AWaitingCodeWithABadFieldApprovesNothing(string field, string json)
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        JsonObject entry = JsonNode.Parse(await File.ReadAllTextAsync(WaitingCodePath("spec"), TestContext.Current.CancellationToken))!.AsObject();
        entry[field] = JsonNode.Parse(json);
        await File.WriteAllTextAsync(WaitingCodePath("spec"), entry.ToJsonString(), TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, _) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task AWaitingCodeCopiedToAnotherPhaseApprovesNothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), "- [ ] T001 [US1] (FR-001) Test placing an order\n", cancellationToken);
        await TypeAsync("spec");
        File.Copy(WaitingCodePath("spec"), WaitingCodePath("tasks"));

        (ExitCode exitCode, _, string error) = await TypeAsync($"tasks {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("no approval code is waiting for the tasks", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));
        Assert.False(File.Exists(WaitingCodePath("tasks")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ALinkInPlaceOfTheWaitingCodeIsRefusedAndNotFollowed(bool codeTyped)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symlinks needs developer mode on Windows.");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        string target = Path.Combine(root.FullName, "elsewhere.json");
        await File.WriteAllTextAsync(target, "untouched", cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(WaitingCodePath("spec"))!);
        File.CreateSymbolicLink(WaitingCodePath("spec"), target);

        (ExitCode exitCode, _, string error) = await TypeAsync(codeTyped ? $"spec {Code}" : "spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("spec.json is a symbolic link", error, StringComparison.Ordinal);
        Assert.Equal("untouched", await File.ReadAllTextAsync(target, cancellationToken));
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AWaitingCodesDirectoryThatIsALinkIsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symlinks needs developer mode on Windows.");
        await CreateProjectAsync();
        DirectoryInfo elsewhere = Directory.CreateDirectory(Path.Combine(root.FullName, "elsewhere"));
        Directory.CreateDirectory(Path.Combine(root.FullName, ".lockflight", "local"));
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, SessionCodes.RelativeDirectory), elsewhere.FullName);

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains($"{SessionCodes.RelativeDirectory} is a symbolic link", error, StringComparison.Ordinal);
        Assert.Empty(elsewhere.GetFileSystemInfos());
    }

    [Fact]
    public async Task ADirectoryInPlaceOfTheWaitingCodeIsRefused()
    {
        await CreateProjectAsync();
        Directory.CreateDirectory(WaitingCodePath("spec"));

        (ExitCode exitCode, _, string error) = await TypeAsync("spec");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("spec.json is a directory", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Fact]
    public async Task AClockThatWentBackDoesNotStretchTheCodesLife()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        time.Advance(TimeSpan.FromHours(-1));

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the code expired", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task AClockSyncStepBackKeepsTheCode()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        time.Advance(TimeSpan.FromSeconds(-30));

        (ExitCode exitCode, _, string error) = await TypeAsync($"spec {Code}");

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Fact]
    public async Task ANewerDigestReplacesTheOlderCode()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        await TypeAsync("spec");

        (ExitCode olderExit, _, string error) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, olderExit);
        Assert.Contains("not the code shown", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));

        (ExitCode newerExit, _, _) = await TypeAsync($"spec {SecondCode}");

        Assert.Equal(ExitCode.Pass, newerExit);
    }

    [Fact]
    public async Task APlanDeletedBetweenTheStepsIsNotApproved()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await CreateProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "plan.md"), "# Plan\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(SpecDirectory, "tasks.md"), "- [ ] T001 [US1] (FR-001) Test placing an order\n", cancellationToken);
        await TypeAsync("tasks");
        File.Delete(Path.Combine(SpecDirectory, "plan.md"));

        (ExitCode exitCode, _, string error) = await TypeAsync($"tasks {Code}");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the tasks changed since you saw it (plan.md)", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("tasks")));
    }

    [Theory]
    [InlineData("spec --feature ../001-orders")]
    [InlineData("spec --feature specs/001-orders")]
    [InlineData("spec --feature specs\\001-orders")]
    [InlineData("spec --feature ..")]
    [InlineData("spec --feature .")]
    [InlineData("spec k7m2qx --feature ../001-orders")]
    [InlineData("spec k7m2qx --feature ./001-orders")]
    public async Task AFeatureGivenAsAPathIsRefused(string arguments)
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("--feature takes a feature ID", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("spec --force")]
    [InlineData("spec --sign=yes")]
    [InlineData("spec --feature")]
    [InlineData("spec --sign --sign")]
    [InlineData("spec k7m2qx extra")]
    [InlineData("spec k7m2qx --yes")]
    [InlineData("spec k7m2qx --feature 001-orders --feature 001-orders")]
    [InlineData("specs k7m2qx")]
    [InlineData("spec-tasks k7m2qx")]
    public async Task AnUnknownOrMalformedArgumentIsAUsageRefusal(string arguments)
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode exitCode, _, string error) = await TypeAsync(arguments);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
        Assert.True(File.Exists(WaitingCodePath("spec")));
    }

    [Fact]
    public async Task ExtraWhitespaceAroundTheWordsIsIgnored()
    {
        await CreateProjectAsync();
        (ExitCode showExit, _, string digest) = await TypeAsync("   spec \t ");

        Assert.Equal(ExitCode.HookBlock, showExit);
        Assert.Contains($"/lockflight:approve spec {Code}", digest, StringComparison.Ordinal);

        (ExitCode recordExit, _, string error) = await TypeAsync($"\tSPEC \n  {Code}   ");

        Assert.True(recordExit == ExitCode.Pass, error);
        Assert.True(File.Exists(RecordPath("spec")));
    }

    [Theory]
    [InlineData("Lockflight:Approve")]
    [InlineData("LOCKFLIGHT:APPROVE")]
    public async Task TheCommandInAnotherCaseIsStillCheckedByTheEngine(string commandName)
    {
        await CreateProjectAsync();

        (ExitCode exitCode, string output, string error) = await TypeAsync("spec", new Dictionary<string, string>(), commandName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains("nothing was approved", error, StringComparison.Ordinal);
        Assert.False(File.Exists(WaitingCodePath("spec")));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("Plugin")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnySourceButThePluginIsRefusedAtBothSteps(string? source)
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode showExit, _, _) = await TypeAsync("spec", AttendedSession, source: source);
        (ExitCode recordExit, _, string error) = await TypeAsync($"spec {SecondCode}", AttendedSession, source: source);

        Assert.Equal(ExitCode.HookBlock, showExit);
        Assert.Equal(ExitCode.HookBlock, recordExit);
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
    public async Task AfterFiveWrongCodesTheShownCodeIsDiscarded()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");
        for (int attempt = 1; attempt < 5; attempt++)
        {
            (_, _, string wrong) = await TypeAsync("spec aaaaaa");
            Assert.Contains("not the code shown", wrong, StringComparison.Ordinal);
        }

        (ExitCode fifthExit, _, string fifth) = await TypeAsync("spec aaaaaa");
        (ExitCode rightExit, _, string afterwards) = await TypeAsync($"spec {Code}");

        Assert.Equal(ExitCode.HookBlock, fifthExit);
        Assert.Contains("5 wrong codes", fifth, StringComparison.Ordinal);
        Assert.False(File.Exists(WaitingCodePath("spec")));
        Assert.Equal(ExitCode.HookBlock, rightExit);
        Assert.Contains("no approval code is waiting", afterwards, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath("spec")));
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
    public async Task TheCommandAloneWhileACodeWaitsSaysWhichLineToType()
    {
        await CreateProjectAsync();
        await TypeAsync("spec");

        (ExitCode exitCode, string output, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.Contains($"nothing was approved yet: a code is waiting for the spec of {FeatureId}, valid until 09:10 UTC", error, StringComparison.Ordinal);
        Assert.Contains("Step 2 of 2 — type: /lockflight:approve spec <the code shown with it>", error, StringComparison.Ordinal);
        Assert.DoesNotContain(Code, error, StringComparison.Ordinal);
        Assert.True(File.Exists(WaitingCodePath("spec")));
        (ExitCode stillWorks, _, _) = await TypeAsync($"spec {Code}");
        Assert.Equal(ExitCode.Pass, stillWorks);
    }

    [Fact]
    public async Task TheCommandAloneWithoutAWaitingCodeShowsTheUsageAndIsNotLedgeredAsABlock()
    {
        await CreateProjectAsync();

        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("type /lockflight:approve spec or /lockflight:approve tasks", error, StringComparison.Ordinal);
        string ledger = string.Concat(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "ledger")).Select(File.ReadAllText));
        Assert.Contains("\"session-command.answered\"", ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("hook.blocked", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnattendedApprovalIsLedgeredAsABlock()
    {
        await CreateProjectAsync();

        await TypeAsync("spec", new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        string ledger = string.Concat(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "ledger")).Select(File.ReadAllText));
        Assert.Contains("session-approval:unattended", ledger, StringComparison.Ordinal);
        Assert.Contains("hook.blocked", ledger, StringComparison.Ordinal);
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
        await File.WriteAllTextAsync(
            SpecPath,
            "# Orders\n\n### User Story 1 - Place an order (Priority: P1)\n\n- **FR-001**: Customers place orders\n- **FR-002**: Totals [NEEDS CLARIFICATION: currency?]\n",
            cancellationToken);
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
            Random = codes,
        };

        ExitCode exitCode = await new HookCommand(context).RunAsync(HookEvent.UserPromptExpansion, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    private async Task<JsonObject> ReadRecordAsync(string phase, CancellationToken cancellationToken) =>
        JsonNode.Parse(await File.ReadAllTextAsync(RecordPath(phase), cancellationToken))!.AsObject();
}
