using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Tests.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class SessionResetTests : IDisposable
{
    private const string Code = "k7m2qx";
    private const string SecondCode = "p3w9ha";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-session-reset-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly RandomSource codes = RandomSource.WithCodes(Code, SecondCode);

    public SessionResetTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f1"));
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    private string RecordPath => RedGreenRecord.PathFor(Feature);

    [Fact]
    public async Task TheFirstStepShowsTheEvidenceThatWouldBeDiscardedAndKeepsIt()
    {
        SaveRecord();

        (ExitCode exitCode, string output, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Empty(output);
        Assert.StartsWith("Step 1 of 2 — nothing has been reset yet. Check the red/green evidence of f1 you are discarding", error, StringComparison.Ordinal);
        Assert.Contains("Baseline: 1 tests at commit abc", error, StringComparison.Ordinal);
        Assert.Contains("Cycle 1: 2 tests, red, test projects locked", error, StringComparison.Ordinal);
        Assert.Contains($"Type within 10 minutes: /lockflight:reset {Code}", error, StringComparison.Ordinal);
        Assert.EndsWith(SessionCommandGate.NotAnError, error, StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath));
        Assert.DoesNotContain(Code, File.ReadAllText(Path.Combine(root.FullName, SessionCodes.RelativeDirectory, "reset.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSecondStepDiscardsTheEvidenceAndLedgersIt()
    {
        SaveRecord();
        await TypeAsync(string.Empty);

        (ExitCode exitCode, string output, string error) = await TypeAsync(Code);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.StartsWith("Step 2 of 2 — reset: the red/green evidence of f1 is discarded", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.Contains("redgreen status --feature f1", output, StringComparison.Ordinal);
        Assert.False(File.Exists(RecordPath));
        string ledger = string.Concat(Directory.GetFiles(Path.Combine(root.FullName, ".lockflight", "ledger")).Select(File.ReadAllText));
        Assert.Contains("redgreen:reset", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvidenceChangedBetweenTheStepsIsNotDiscarded()
    {
        SaveRecord();
        await TypeAsync(string.Empty);
        File.AppendAllText(RecordPath, " ");

        (ExitCode exitCode, _, string error) = await TypeAsync(Code);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was reset: the red/green evidence of f1 changed since you saw it", error, StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath));
    }

    [Fact]
    public async Task AWrongCodeDiscardsNothing()
    {
        SaveRecord();
        await TypeAsync(string.Empty);

        (ExitCode exitCode, _, string error) = await TypeAsync("aaaaaa");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("that is not the code shown with the reset", error, StringComparison.Ordinal);
        Assert.Contains("redgreen reset", error, StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath));
    }

    [Fact]
    public async Task ACodeShownForAnotherFeatureDiscardsNothing()
    {
        SaveRecord();
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f2"));
        await TypeAsync("--feature f1");

        (ExitCode exitCode, _, string error) = await TypeAsync($"{Code} --feature f2");

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("the code was shown for feature f1, not f2", error, StringComparison.Ordinal);
        Assert.True(File.Exists(RecordPath));
    }

    [Fact]
    public async Task AResetWithoutEvidenceIsRefused()
    {
        (ExitCode exitCode, _, string error) = await TypeAsync(string.Empty);

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("f1 has no red/green evidence to discard", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResetOutsideAnAttendedSessionIsRefused()
    {
        SaveRecord();

        (ExitCode exitCode, _, string error) = await SessionTyping.TypeAsync(
            root.FullName, time, codes, SessionReset.CommandName, string.Empty, new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

        Assert.Equal(ExitCode.HookBlock, exitCode);
        Assert.Contains("nothing was reset: /lockflight:reset discards red/green evidence only when you type it", error, StringComparison.Ordinal);
        Assert.Contains("redgreen reset", error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(root.FullName, SessionCodes.RelativeDirectory, "reset.json")));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private Task<(ExitCode ExitCode, string Output, string Error)> TypeAsync(string arguments) =>
        SessionTyping.TypeAsync(root.FullName, time, codes, SessionReset.CommandName, arguments);

    private void SaveRecord()
    {
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null,
            [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, []), new RedGreenTest("T:A.D", "A.D", RedClass.RedStub, [])]));
        record.Save(RecordPath, Feature.Id);
    }
}
