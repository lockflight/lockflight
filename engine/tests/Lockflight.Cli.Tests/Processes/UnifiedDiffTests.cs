using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Processes;

public sealed class UnifiedDiffTests
{
    [Fact]
    public void AnAddedLineThatLooksLikeAFileHeaderStaysInItsFile()
    {
        const string diff = """
            diff --git a/notes.md b/notes.md
            --- a/notes.md
            +++ b/notes.md
            @@ -0,0 +1,2 @@
            +++ b/other.md
            +gitleaks:allow
            """;

        FileDiff file = Assert.Single(UnifiedDiff.FilesOf(diff));

        Assert.Equal("notes.md", file.File);
        Assert.Equal(["++ b/other.md", "gitleaks:allow"], file.Added.Select(line => line.Text));
        Assert.Equal([1, 2], file.Added.Select(line => line.Line));
    }

    [Fact]
    public void QuotedAndTabTerminatedNamesAreReadAsGitMeantThem()
    {
        const string diff = "+++ \"b/sp\\303\\251c/a\\tb.cs\"\n@@ -0,0 +1 @@\n+x\n+++ b/with space.cs\t\n@@ -0,0 +1 @@\n+y\n";

        string[] files = [.. UnifiedDiff.FilesOf(diff).Select(file => file.File)];

        Assert.Equal(["spéc/a\tb.cs", "with space.cs"], files);
    }

    [Fact]
    public void ADeletedFileIsLeftOutAndRemovedLinesDoNotCount()
    {
        const string diff = """
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -gone
            -too
            +++ b/kept.cs
            @@ -3,2 +3 @@
            -old one
            -old two
            +new
            """;

        FileDiff file = Assert.Single(UnifiedDiff.FilesOf(diff));

        Assert.Equal("kept.cs", file.File);
        Assert.Equal(["new"], file.Added.Select(line => line.Text));
        Assert.Equal([3], file.Added.Select(line => line.Line));
        Assert.Equal([3, 4], file.Removed.Select(line => line.Line));
    }

    [Theory]
    [InlineData("-﻿class A {}\n+class A {}\n", true)]
    [InlineData("-class A {}\n+﻿class A {}\n", true)]
    [InlineData("-﻿class A {}\n+﻿class B {}\n", false)]
    public void AByteOrderMarkAddedOrRemovedOnLineOneIsAnEncodingChange(string hunk, bool expected)
    {
        FileDiff file = Assert.Single(UnifiedDiff.FilesOf($"+++ b/A.cs\n@@ -1 +1 @@\n{hunk}"));

        Assert.Equal(expected, file.ChangesByteOrderMark);
    }

    [Fact]
    public void PatchFilesKeepDeletedFilesWithTheirRemovedLines()
    {
        const string diff = "--- a/gone.cs\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-class Gone\n-{ }\n--- /dev/null\n+++ b/new.cs\n@@ -0,0 +1 @@\n+class New { }\n";

        IReadOnlyList<PatchFile> files = UnifiedDiff.PatchFilesOf(diff);

        Assert.Equal([("gone.cs", (string?)null), (null, "new.cs")], files.Select(file => (file.OldFile, file.NewFile)));
        Assert.Equal(["class Gone", "{ }"], files[0].Removed.Select(line => line.Text));
        Assert.Equal([1, 2], files[0].Removed.Select(line => line.Line));
        Assert.Equal(["new.cs"], UnifiedDiff.FilesOf(diff).Select(file => file.File));
    }
}
