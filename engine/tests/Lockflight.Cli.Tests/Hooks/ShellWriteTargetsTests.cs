using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellWriteTargetsTests
{
    [Theory]
    [InlineData("echo hi > secrets/key.txt", "secrets/key.txt")]
    [InlineData("echo hi >> 'notes with space.md'", "notes with space.md")]
    [InlineData("cat a | tee -a out.log", "out.log")]
    [InlineData("cp src/a.cs lockflight.yaml", "lockflight.yaml")]
    [InlineData("mv -f tmp global.json", "global.json")]
    [InlineData("mv tests/A.cs /tmp/A.cs", "tests/A.cs")]
    [InlineData("rm -rf .lockflight/lockdown", ".lockflight/lockdown")]
    [InlineData("sed -i '' 's/a/b/' Directory.Build.props", "Directory.Build.props")]
    [InlineData("dotnet build && echo done > build.log", "build.log")]
    [InlineData("perl -pi -e 's/a/b/' tests/A.cs", "tests/A.cs")]
    [InlineData("perl -0pi -e 's/a/b/' tests/A.cs", "tests/A.cs")]
    [InlineData("perl -i.bak -pe 's/a/b/' tests/A.cs", "tests/A.cs")]
    [InlineData("perl -pi fix.pl tests/A.cs", "tests/A.cs")]
    [InlineData("git checkout HEAD -- tests/A.cs", "tests/A.cs")]
    [InlineData("git checkout HEAD~1 tests/A.cs", "tests/A.cs")]
    [InlineData("git checkout tests/A.cs", "tests/A.cs")]
    [InlineData("git restore tests/A.cs", "tests/A.cs")]
    [InlineData("git restore --source HEAD~2 --staged --worktree tests/A.cs", "tests/A.cs")]
    [InlineData("git stash push -- tests/A.cs", "tests/A.cs")]
    [InlineData("git stash -- tests/A.cs", "tests/A.cs")]
    [InlineData("git stash push -m wip tests/A.cs", "tests/A.cs")]
    [InlineData("git -C tests checkout -- A.cs", "tests/A.cs")]
    [InlineData("(cd tests && rm T.cs)", "T.cs")]
    public void VisibleWritesAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("dotnet format", ".")]
    [InlineData("dotnet format whitespace --folder", ".")]
    [InlineData("dotnet format tests/Shop.Tests/Shop.Tests.csproj", "tests/Shop.Tests")]
    [InlineData("dotnet format Shop.sln --severity warn", ".")]
    [InlineData("dotnet format --include tests/A.cs src/B.cs", "tests/A.cs")]
    [InlineData("git restore .", ".")]
    [InlineData("git checkout -- tests", "tests")]
    [InlineData("git stash", ".")]
    [InlineData("git stash pop", ".")]
    [InlineData("git reset --hard HEAD", ".")]
    [InlineData("git clean -fd", ".")]
    [InlineData("rm -rf tests/Shop.Tests", "tests/Shop.Tests")]
    public void CommandsThatRewriteADirectoryNameIt(string command, string expectedTree)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.Contains(expectedTree, writes.Trees.Concat(writes.SourceTrees));
    }

    [Fact]
    public void DotnetFormatRewritesSourceFilesOnly()
    {
        ShellWrites writes = ShellWriteTargets.Find("dotnet format", ShellDialect.Posix);

        Assert.Empty(writes.Trees);
        Assert.Equal(["."], writes.SourceTrees);
    }

    [Theory]
    [InlineData("Set-Content -Path global.json -Value x", "global.json")]
    [InlineData("Remove-Item .lockflight\\lockdown", ".lockflight\\lockdown")]
    [InlineData("'x' | Out-File -FilePath C:\\repo\\secrets\\k.txt", "C:\\repo\\secrets\\k.txt")]
    [InlineData("Set-Content -Path tests\\T.cs -Value x", "tests\\T.cs")]
    public void PowerShellWritesKeepBackslashPaths(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Files);

    [Theory]
    [InlineData("dotnet test --no-build")]
    [InlineData("git status && git diff")]
    [InlineData("git checkout -b feature/x origin/main")]
    [InlineData("git checkout HEAD~1")]
    [InlineData("git switch main")]
    [InlineData("git pull --rebase && git merge main")]
    [InlineData("echo '>' secrets/k")]
    [InlineData("cmd 2>&1 | tee")]
    [InlineData("cat <<'EOF'\nrm secrets/k\nEOF")]
    [InlineData("# rm secrets/k")]
    [InlineData("sed 's/a/b/' tests/A.cs")]
    [InlineData("sed -n -e 's/i/x/' tests/A.cs")]
    [InlineData("tar -czf out.tgz src")]
    [InlineData("unzip -l a.zip")]
    [InlineData("git apply --check fix.patch")]
    [InlineData("git rm --cached tests/A.cs")]
    [InlineData("git stash list")]
    [InlineData("git reset HEAD~1")]
    [InlineData("dotnet format --verify-no-changes")]
    [InlineData("perl -ne 'print if /x/' tests/A.cs")]
    [InlineData("cat lockflight.yaml")]
    [InlineData("grep -r secrets src")]
    [InlineData("cd tests && ls")]
    public void ReadOnlyCommandsHaveNoTargets(string command)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.Empty(writes.Files);
        Assert.Empty(writes.Trees);
    }
}
