using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Tests.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.RedGreen;

public sealed class ResetSnapshotRefsTests : IDisposable
{
    private const string Code = "k7m2qx";

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-reset-refs-");
    private readonly ManualTime time = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    public ResetSnapshotRefsTests()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f1"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "specs", "f2"));
    }

    private Feature Feature => new("f1", Path.Combine(root.FullName, "specs", "f1"), Path.Combine(root.FullName, ".lockflight", "features", "f1"));

    [Fact]
    public async Task AResetDeletesTheCyclesSnapshotRefsAndNoOthers()
    {
        (string own, string other) = await SnapshotsAsync();

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["redgreen", "reset", "--feature", "f1"], string.Empty, root.FullName, time);

        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains($"Deleted the red-time snapshot ref {own}.", output, StringComparison.Ordinal);
        Assert.False(await RefExistsAsync(own));
        Assert.True(await RefExistsAsync(other));
    }

    [Fact]
    public async Task TheSessionResetShowsTheSnapshotRefsFirstAndDeletesThemSecond()
    {
        (string own, _) = await SnapshotsAsync();
        RandomSource codes = RandomSource.WithCodes(Code);

        (_, _, string shown) = await SessionTyping.TypeAsync(root.FullName, time, codes, SessionReset.CommandName, "--feature f1");
        bool keptAfterTheFirstStep = await RefExistsAsync(own);
        (ExitCode exitCode, string output, string error) = await SessionTyping.TypeAsync(root.FullName, time, codes, SessionReset.CommandName, $"{Code} --feature f1");

        Assert.Contains($"Red-time snapshot {own} (deleted)", shown, StringComparison.Ordinal);
        Assert.True(keptAfterTheFirstStep);
        Assert.True(exitCode == ExitCode.Pass, error);
        Assert.Contains($"Deleted the red-time snapshot ref {own}.", SessionTyping.SystemMessage(output), StringComparison.Ordinal);
        Assert.False(await RefExistsAsync(own));
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    /// <summary>A snapshot ref for cycle 1 of f1 (in its record) and one for f2 (not), both of a real commit.</summary>
    private async Task<(string Own, string Other)> SnapshotsAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await ProcessRunner.RunAsync("git", ["init", "-q"], root.FullName, cancellationToken);
        await ProcessRunner.RunAsync("git", ["config", "user.email", "person@example.com"], root.FullName, cancellationToken);
        RedSnapshot snapshot = await RedSnapshots.TakeAsync(root.FullName, [root.FullName], TimeLimit, cancellationToken);
        string own = (await RedSnapshots.KeepAsync(root.FullName, "f1", 1, snapshot.Commit!, TimeLimit, cancellationToken))!;
        string other = (await RedSnapshots.KeepAsync(root.FullName, "f2", 1, snapshot.Commit!, TimeLimit, cancellationToken))!;
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string>(),
            TestProjects = ["tests/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, [])])
        {
            Snapshot = snapshot.Commit,
        });
        record.Save(RedGreenRecord.PathFor(Feature), Feature.Id);
        return (own, other);
    }

    private async Task<bool> RefExistsAsync(string reference) =>
        (await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "--quiet", reference], root.FullName, TestContext.Current.CancellationToken)).ExitCode == 0;
}
