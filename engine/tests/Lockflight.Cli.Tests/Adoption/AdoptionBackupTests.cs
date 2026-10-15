using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class AdoptionBackupTests : IDisposable
{
    private const string Id = "20261012000000000";

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-backup-");

    private readonly DirectoryInfo outside = Directory.CreateTempSubdirectory("lockflight-outside-");

    [Fact]
    public void UndoRevertsWhatApplyChangedAndKeepsLaterWork()
    {
        Write("CLAUDE.md", "# Team\n");
        Write("lockflight.yaml", "version: 1\n");
        Write("specs/001-a/spec.md", "spec\n");
        AdoptionBackup.Create(root.FullName, Id);

        // What apply does.
        Write("lockflight.yaml", "version: 1\nprotected:\n  - \"a/**\"\n");
        Write("CLAUDE.md", "# Team\n<!-- lockflight:begin -->\n");
        Write(".lockflight/baselines/adopted-features.json", "{}");
        AdoptionBackup.RecordApplied(root.FullName, Id);

        // Later work: a new feature, a worktree, and an edit to a file apply had changed.
        Write("specs/012-new/spec.md", "a week of work\n");
        Write(".claude/worktrees/agent-1/src/App.cs", "uncommitted\n");
        Write("CLAUDE.md", "# Team\n<!-- lockflight:begin -->\nmy own note\n");
        UndoResult result = AdoptionBackup.Undo(root.FullName, Id, force: false);

        Assert.Equal("version: 1\n", Read("lockflight.yaml"));
        Assert.False(File.Exists(Path.Combine(root.FullName, ".lockflight", "baselines", "adopted-features.json")));
        Assert.Equal("a week of work\n", Read("specs/012-new/spec.md"));
        Assert.Equal("uncommitted\n", Read(".claude/worktrees/agent-1/src/App.cs"));
        Assert.Equal("# Team\n<!-- lockflight:begin -->\nmy own note\n", Read("CLAUDE.md"));
        Assert.Equal(["CLAUDE.md"], result.Kept);
        Assert.Equal((1, 1), (result.Restored, result.Removed));
    }

    [Fact]
    public void ForceRevertsFilesChangedAfterApplyToo()
    {
        Write("CLAUDE.md", "# Team\n");
        AdoptionBackup.Create(root.FullName, Id);
        Write("CLAUDE.md", "# Team\nblock\n");
        AdoptionBackup.RecordApplied(root.FullName, Id);
        Write("CLAUDE.md", "# Team\nblock\nlater\n");

        UndoResult result = AdoptionBackup.Undo(root.FullName, Id, force: true);

        Assert.Empty(result.Kept);
        Assert.Equal("# Team\n", Read("CLAUDE.md"));
    }

    [Fact]
    public void LinksAndOtherSessionsWorktreesAreNeverArchivedOrWritten()
    {
        File.WriteAllText(Path.Combine(outside.FullName, "shared.md"), "team skill\n");
        Directory.CreateDirectory(Path.Combine(root.FullName, ".claude", "skills"));
        Directory.CreateSymbolicLink(Path.Combine(root.FullName, ".claude", "skills", "shared"), outside.FullName);
        Write(".claude/worktrees/agent-1/a.cs", "checkout\n");
        Write(".claude/settings.local.json", "{\"token\":\"x\"}");
        Write(".claude/hooks/guard.sh", "exit 0\n");
        AdoptionBackup.Create(root.FullName, Id);
        AdoptionBackup.RecordApplied(root.FullName, Id);

        File.WriteAllText(Path.Combine(outside.FullName, "added-later.md"), "elsewhere\n");
        AdoptionBackup.RestoreAll(root.FullName, Id);

        Assert.True(File.Exists(Path.Combine(outside.FullName, "added-later.md")));
        Assert.True(File.Exists(Path.Combine(root.FullName, ".claude", "worktrees", "agent-1", "a.cs")));
        Assert.Equal("{\"token\":\"x\"}", Read(".claude/settings.local.json"));
    }

    [Fact]
    public void RestoreAllRollsBackAFailedApplyExactly()
    {
        Write("CLAUDE.md", "# Team\n");
        AdoptionBackup.Create(root.FullName, Id);
        Write("CLAUDE.md", "half-written");
        Write(".specify/templates/new.md", "added by a failed merge\n");

        AdoptionBackup.RestoreAll(root.FullName, Id);

        Assert.Equal("# Team\n", Read("CLAUDE.md"));
        Assert.False(File.Exists(Path.Combine(root.FullName, ".specify", "templates", "new.md")));
    }

    [Fact]
    public void IdsAreUniqueWithinTheSameMillisecond()
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        string first = AdoptionBackup.NewId(root.FullName, now);
        AdoptionBackup.Create(root.FullName, first);

        Assert.NotEqual(first, AdoptionBackup.NewId(root.FullName, now));
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(root.FullName, relative));

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(outside);
    }
}
