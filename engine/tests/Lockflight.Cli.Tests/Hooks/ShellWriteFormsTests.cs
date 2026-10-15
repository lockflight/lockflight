using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellWriteFormsTests
{
    [Theory]
    [InlineData("echo ok\nrm secrets/k", "secrets/k")]
    [InlineData("echo ok\r\nrm secrets/k", "secrets/k")]
    [InlineData("dotnet build 2> build.err", "build.err")]
    [InlineData("dotnet build 2>> secrets/k", "secrets/k")]
    [InlineData("echo x 1>> secrets/k", "secrets/k")]
    [InlineData("echo x &> secrets/k", "secrets/k")]
    [InlineData("echo x &>> secrets/k", "secrets/k")]
    [InlineData("echo x >| secrets/k", "secrets/k")]
    [InlineData("echo x 3> secrets/k", "secrets/k")]
    [InlineData("echo x>secrets/k", "secrets/k")]
    [InlineData("echo x 2>&1 >secrets/k", "secrets/k")]
    [InlineData("echo x >&secrets/k", "secrets/k")]
    [InlineData("cp x secrets/", "secrets/x")]
    [InlineData("cp a/x b/lockflight.yaml .", "lockflight.yaml")]
    [InlineData("cp -t secrets a/k", "secrets/k")]
    [InlineData("cp --target-directory=secrets a/k", "secrets/k")]
    [InlineData("cd a && cp k ../secrets", "a/../secrets/k")]
    [InlineData("install -m 644 k secrets", "secrets/k")]
    [InlineData("ln -s /tmp/k secrets", "secrets/k")]
    [InlineData("mv k secrets", "secrets/k")]
    [InlineData("sed -Ei 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed -ni 's/a/b/p' secrets/k", "secrets/k")]
    [InlineData("sed -i.bak 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed --in-place 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed --in-place=.bak -e 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed -i -e 's/a/b/' -e 's/c/d/' secrets/k", "secrets/k")]
    [InlineData("perl -i -pe 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("command rm secrets/k", "secrets/k")]
    [InlineData("env A=1 B=2 rm secrets/k", "secrets/k")]
    [InlineData("sudo -u root rm secrets/k", "secrets/k")]
    [InlineData("time nice -n 5 nohup rm secrets/k", "secrets/k")]
    [InlineData("! rm secrets/k", "secrets/k")]
    [InlineData("exec rm secrets/k", "secrets/k")]
    [InlineData("if true; then rm secrets/k; fi", "secrets/k")]
    [InlineData("for f in a; do rm secrets/k; done", "secrets/k")]
    [InlineData("{ rm secrets/k; }", "secrets/k")]
    [InlineData("( rm secrets/k )", "secrets/k")]
    [InlineData("echo $(rm secrets/k)", "secrets/k")]
    [InlineData("echo \"$(rm secrets/k)\"", "secrets/k")]
    [InlineData("echo `rm secrets/k`", "secrets/k")]
    [InlineData("bash -c 'rm secrets/k'", "secrets/k")]
    [InlineData("sh -ec \"echo x > secrets/k\"", "secrets/k")]
    [InlineData("eval rm secrets/k", "secrets/k")]
    [InlineData("dd if=/dev/zero of=secrets/k", "secrets/k")]
    [InlineData("git rm secrets/k", "secrets/k")]
    [InlineData("git mv a secrets/k", "secrets/k")]
    [InlineData("curl -o secrets/k https://example.com", "secrets/k")]
    [InlineData("touch secrets/k && truncate -s 0 secrets/k", "secrets/k")]
    [InlineData("rm a \\\n  secrets/k", "secrets/k")]
    public void PosixWriteFormsAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("cp -r a secrets", "secrets")]
    [InlineData("cp $FILES secrets", "secrets")]
    [InlineData("mv a secrets", "secrets")]
    [InlineData("rsync -a a/ secrets", "secrets")]
    [InlineData("tar -xzf a.tgz -C secrets", "secrets")]
    [InlineData("tar xf a.tar", ".")]
    [InlineData("unzip -o a.zip -d secrets", "secrets")]
    [InlineData("unzip a.zip", ".")]
    [InlineData("patch -p1 < fix.patch", ".")]
    [InlineData("patch -d secrets -p1 < fix.patch", "secrets")]
    [InlineData("git apply fix.patch", ".")]
    [InlineData("git checkout .", ".")]
    [InlineData("git checkout test", "test")]
    [InlineData("git checkout -f main", ".")]
    [InlineData("git switch -f main", ".")]
    [InlineData("git switch --discard-changes main", ".")]
    [InlineData("git read-tree -u -m HEAD", ".")]
    [InlineData("find secrets -exec rm {} \\;", "secrets")]
    [InlineData("rm -rf *", "*")]
    public void CommandsThatRewriteADirectoryNameIt(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.Posix).Trees);

    [Theory]
    [InlineData("Set-Content -Path:secrets\\k -Value x", "secrets\\k")]
    [InlineData("Out-File -FilePath:secrets\\k", "secrets\\k")]
    [InlineData("Copy-Item a\\k -Destination:secrets", "secrets/k")]
    [InlineData("Copy-Item -LiteralPath:a\\k secrets", "secrets/k")]
    [InlineData("Copy-Item a\\k secrets", "secrets/k")]
    [InlineData("cp a\\k secrets", "secrets/k")]
    [InlineData("Move-Item a secrets\\k", "secrets\\k")]
    [InlineData("Rename-Item secrets\\a -NewName k", "secrets/k")]
    [InlineData("'x' | Tee-Object -FilePath secrets\\k", "secrets\\k")]
    [InlineData("Set-Item -Path secrets\\k -Value x", "secrets\\k")]
    [InlineData("Add-Content secrets\\k x", "secrets\\k")]
    [InlineData("& { Remove-Item secrets\\k }", "secrets\\k")]
    [InlineData("Write-Host ok\nRemove-Item secrets\\k", "secrets\\k")]
    [InlineData("Write-Host $(Remove-Item secrets\\k)", "secrets\\k")]
    [InlineData("New-Item -Path secrets -Name k", "secrets/k")]
    [InlineData("Set-Content -Pa secrets\\k -Va x", "secrets\\k")]
    [InlineData("pwsh -Command Remove-Item secrets\\k", "secrets\\k")]
    public void PowerShellWriteFormsAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Files);

    [Theory]
    [InlineData("Move-Item secrets old", "secrets")]
    [InlineData("Rename-Item secrets old", "secrets")]
    [InlineData("Copy-Item a secrets -Recurse", "secrets")]
    public void PowerShellMovesOfDirectoriesAreTrees(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Trees);

    [Theory]
    [InlineData("cd $X && rm k", "k")]
    [InlineData("cd ~ && rm k", "k")]
    [InlineData("cd - && rm k", "k")]
    [InlineData("popd && rm k", "k")]
    [InlineData("cd src* && rm k", "k")]
    [InlineData("cd `pwd` && rm k", "k")]
    [InlineData("cd $(pwd)/a && cd b && rm k", "b/k")]
    public void RelativePathsAfterACdThatCannotBeFollowedFloat(string command, string floating) =>
        Assert.Contains(floating, ShellWriteTargets.Find(command, ShellDialect.Posix).FloatingFiles);

    [Theory]
    [InlineData("cd src && rm k")]
    [InlineData("pushd src && rm k && popd && rm j")]
    [InlineData("cd /abs && rm k")]
    [InlineData("rm /abs/k")]
    public void PathsAfterAFollowedCdDoNotFloat(string command) =>
        Assert.Empty(ShellWriteTargets.Find(command, ShellDialect.Posix).FloatingFiles);

    [Theory]
    [InlineData("ls | xargs rm")]
    [InlineData("git ls-files | xargs -0 -n 1 sed -i 's/a/b/'")]
    [InlineData("cat list | xargs -I{} cp {} out")]
    [InlineData("grep -r Foo web | xargs rm")]
    [InlineData("grep -rl Foo $(pwd) | xargs sed -i 's/a/b/'")]
    public void AWriterFedByXargsHasHiddenTargets(string command) =>
        Assert.NotNull(ShellWriteTargets.Find(command, ShellDialect.Posix).Hidden);

    [Theory]
    [InlineData("grep -rl Foo web | xargs sed -i 's/Foo/Bar/g'", "web")]
    [InlineData("grep -l -e Foo -r src/app | xargs sed -i 's/a/b/'", "src/app")]
    [InlineData("find src -name '*.cs' | xargs -0 sed -i 's/a/b/'", "src")]
    [InlineData("grep -rl Foo | xargs rm", ".")]
    public void FilesListedByGrepOrFindAreConfinedToTheirTree(string command, string tree)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.Null(writes.Hidden);
        Assert.Contains(tree, writes.Trees);
    }

    [Fact]
    public void XargsWithAReaderHidesNothing() =>
        Assert.Null(ShellWriteTargets.Find("ls | xargs grep x", ShellDialect.Posix).Hidden);

    [Fact]
    public void ASubshellTargetCarriesNoParenthesis()
    {
        ShellWrites writes = ShellWriteTargets.Find("(cd tests && echo x > T.cs)", ShellDialect.Posix);

        Assert.Contains("tests/T.cs", writes.Files);
        Assert.DoesNotContain(writes.Files, file => file.Contains(')', StringComparison.Ordinal));
    }

    [Fact]
    public void ACdInsideASubshellDoesNotMoveTheCommandsAfterIt()
    {
        ShellWrites writes = ShellWriteTargets.Find("(cd tests) && echo x > T.cs", ShellDialect.Posix);

        Assert.Equal(["T.cs"], writes.Files);
    }

    [Fact]
    public void APathEntirelyComputedByASubstitutionIsHidden() =>
        Assert.NotNull(ShellWriteTargets.Find("rm \"$(pwd)\"", ShellDialect.Posix).Hidden);

    [Theory]
    [InlineData("echo x > \"logs/$(date +%F).log\"", "logs/*.log")]
    [InlineData("touch \"build-$(date +%s).log\"", "build-*.log")]
    public void ASubstitutionInsideLiteralTextIsAWildcard(string command, string expected) =>
        Assert.Contains(expected, ShellWriteTargets.Find(command, ShellDialect.Posix).ComputedFiles);

    [Theory]
    [InlineData("find . -name '*.json' -delete", "**/*.json")]
    [InlineData("find src -type f -iname '*.tmp' -delete", "src/**/*.tmp")]
    public void AFindDeleteLimitedByNameDeletesThoseNamesOnly(string command, string pattern) =>
        Assert.Contains(pattern, ShellWriteTargets.Find(command, ShellDialect.Posix).PatternFiles);

    [Theory]
    [InlineData("echo x > secrets/{a,b}", "secrets/b")]
    [InlineData("echo x > $'secrets/k'", "secrets/k")]
    [InlineData("echo x > $'secrets\\x2fk'", "secrets/k")]
    [InlineData("busybox rm secrets/k", "secrets/k")]
    [InlineData("git worktree add -b wip secrets", "secrets")]
    public void ExpandedSpellingsAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);
}
