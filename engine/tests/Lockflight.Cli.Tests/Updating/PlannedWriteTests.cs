using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Tests.Updating;

public sealed class PlannedWriteTests : IDisposable
{
    private readonly DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-planned-write-");

    [Fact]
    public async Task ACancelledUpdatePutsBackWhatItAlreadyWroteAndLeavesNoTemporaryFile()
    {
        string removed = Path.Combine(project.FullName, "removed.txt");
        string added = Path.Combine(project.FullName, "added.txt");
        File.WriteAllText(removed, "old\n");
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PlannedWrite.ApplyAllAsync(
            [new PlannedWrite("removed.txt", removed, "old\n", NewText: null), new PlannedWrite("added.txt", added, OldText: null, "new\n")],
            cancel.Token));

        Assert.Equal("old\n", File.ReadAllText(removed));
        Assert.False(File.Exists(added));
        Assert.Equal(["removed.txt"], Directory.EnumerateFiles(project.FullName).Select(Path.GetFileName));
    }

    public void Dispose() => project.Delete(recursive: true);
}
