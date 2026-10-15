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

    private string WaitingCodePath(string phase) => Path.Combine(root.FullName, ApprovalCodes.RelativeDirectory, $"{phase}.json");

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
        Assert.Contains($"approve the spec of {FeatureId}", error, StringComparison.Ordinal);
        Assert.Contains($"spec.md    {ArtifactHash.Of(SpecPath)[..12]}", error, StringComparison.Ordinal);
        Assert.Contains("Requirements (2): FR-001, FR-002", error, StringComparison.Ordinal);
        Assert.Contains("User stories (1): 1 Place an order", error, StringComparison.Ordinal);
        Assert.Contains("Open [NEEDS CLARIFICATION] markers: spec.md line 6", error, StringComparison.Ordinal);
        Assert.Contains("Current approval: none", error, StringComparison.Ordinal);
        Assert.Contains($"/lockflight:approve spec {Code}", error, StringComparison.Ordinal);
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
        Assert.Equal($"Approved spec of {FeatureId}: spec.md {ArtifactHash.Of(SpecPath)[..12]}", hookOutput.RootElement.GetProperty("systemMessage").GetString());
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
        time.Advance(ApprovalCodes.Lifetime);

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
        Assert.Contains("(signed)", output, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(RecordPath("spec"))!, "*.unsigned*"));
        (ExitCode verifyExit, string verifyOutput, _) = await TestConsole.RunAsync(
            ["approve", "verify", "--feature", FeatureId, "--require", "spec", "--allowed-signers", signers], string.Empty, root.FullName);
        Assert.Equal(ExitCode.Pass, verifyExit);
        Assert.Contains("signed by", verifyOutput, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

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
        string arguments, IReadOnlyDictionary<string, string> environment, string commandName = SessionApproval.CommandName, string source = "plugin")
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["session_id"] = "session-1",
            ["cwd"] = root.FullName,
            ["hook_event_name"] = "UserPromptExpansion",
            ["expansion_type"] = "slash_command",
            ["command_name"] = commandName,
            ["command_args"] = arguments,
            ["command_source"] = source,
            ["prompt"] = $"/{commandName} {arguments}",
        });
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
