using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>The common shell writes the guard parses, and the forms it deliberately does not follow.</summary>
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
    [InlineData("cat <<'EOF' > secrets/k\nrm other\nEOF", "secrets/k")]
    [InlineData("echo x | tee secrets/k", "secrets/k")]
    [InlineData("echo x | tee -a secrets/k", "secrets/k")]
    [InlineData("cp x secrets/", "secrets/x")]
    [InlineData("cp a/x b/lockflight.yaml .", "lockflight.yaml")]
    [InlineData("cp -t secrets a/k", "secrets/k")]
    [InlineData("cp --target-directory=secrets a/k", "secrets/k")]
    [InlineData("install -m 644 k secrets", "secrets/k")]
    [InlineData("mv k secrets", "secrets/k")]
    [InlineData("mv secrets/k elsewhere", "secrets/k")]
    [InlineData("sed -Ei 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed -ni 's/a/b/p' secrets/k", "secrets/k")]
    [InlineData("sed -i.bak 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed --in-place 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed --in-place=.bak -e 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("sed -i -e 's/a/b/' -e 's/c/d/' secrets/k", "secrets/k")]
    [InlineData("perl -i -pe 's/a/b/' secrets/k", "secrets/k")]
    [InlineData("perl -pi -e 's/a/b/' secrets/k", "secrets/k")]
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
    [InlineData("bash -ce \"rm secrets/k\"", "secrets/k")]
    [InlineData("sh -ec \"echo x > secrets/k\"", "secrets/k")]
    [InlineData("bash -o pipefail -c 'rm secrets/k'", "secrets/k")]
    [InlineData("eval rm secrets/k", "secrets/k")]
    [InlineData("git rm secrets/k", "secrets/k")]
    [InlineData("git mv a secrets/k", "secrets/k")]
    [InlineData("git checkout HEAD -- secrets/k", "secrets/k")]
    [InlineData("git restore secrets/k", "secrets/k")]
    [InlineData("git restore --source HEAD~1 -- secrets/k", "secrets/k")]
    [InlineData("git -C a checkout -- k", "a/k")]
    [InlineData("touch secrets/k && truncate -s 0 secrets/k", "secrets/k")]
    [InlineData("unlink secrets/k", "secrets/k")]
    [InlineData("rm a \\\n  secrets/k", "secrets/k")]
    public void PosixWriteFormsAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("cp -r a secrets", "secrets")]
    [InlineData("mv a secrets", "secrets")]
    [InlineData("rm -rf secrets", "secrets")]
    [InlineData("git apply fix.patch", ".")]
    [InlineData("git am 0001-fix.patch", ".")]
    [InlineData("git checkout .", ".")]
    [InlineData("git checkout test", "test")]
    [InlineData("git checkout -f main", ".")]
    [InlineData("git switch -f main", ".")]
    [InlineData("git switch --discard-changes main", ".")]
    [InlineData("git reset --hard", ".")]
    [InlineData("git stash", ".")]
    [InlineData("git clean -fd", ".")]
    [InlineData("rm -rf *", "*")]
    public void CommandsThatRewriteADirectoryNameIt(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.Posix).Trees);

    [Theory]
    [InlineData("dotnet format", ".")]
    [InlineData("dotnet format src/App/App.csproj", "src/App")]
    [InlineData("dotnet format --include tests/A.cs", "tests/A.cs")]
    public void DotnetFormatRewritesSourceTrees(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.Posix).SourceTrees);

    [Theory]
    [InlineData("Set-Content -Path:secrets\\k -Value x", "secrets\\k")]
    [InlineData("Out-File -FilePath:secrets\\k", "secrets\\k")]
    [InlineData("'x' > secrets\\k", "secrets\\k")]
    [InlineData("Copy-Item a\\k -Destination:secrets", "secrets/k")]
    [InlineData("Copy-Item -LiteralPath:a\\k secrets", "secrets/k")]
    [InlineData("Copy-Item a\\k secrets", "secrets/k")]
    [InlineData("cp a\\k secrets", "secrets/k")]
    [InlineData("Move-Item a secrets\\k", "secrets\\k")]
    [InlineData("'x' | Tee-Object -FilePath secrets\\k", "secrets\\k")]
    [InlineData("Add-Content secrets\\k x", "secrets\\k")]
    [InlineData("Remove-Item secrets\\a,secrets\\k", "secrets\\k")]
    [InlineData("Remove-Item FileSystem::secrets\\k", "secrets\\k")]
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
    [InlineData("Copy-Item a secrets -Recurse", "secrets")]
    [InlineData("Remove-Item -Recurse secrets", "secrets")]
    public void PowerShellMovesOfDirectoriesAreTrees(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Trees);

    /// <summary>
    /// What the guard does not follow: such a command shows no write, so it is allowed. Test-file hashes at green and review, and the
    /// CI gates, catch what it misses.
    /// </summary>
    [Theory]
    [InlineData("python3 -c \"open('secrets/k', 'w')\"")]
    [InlineData("node -e \"require('fs').writeFileSync('secrets/k', '')\"")]
    [InlineData("bash script.sh")]
    [InlineData("make clean")]
    [InlineData("ls | xargs rm")]
    [InlineData("find secrets -delete")]
    [InlineData("find . -name '*.cs' -exec rm {} \\;")]
    [InlineData("bash <<'EOF'\nrm secrets/k\nEOF")]
    [InlineData("cat x.sh | bash")]
    [InlineData("bash -c \"bash -c 'rm secrets/k'\"")]
    [InlineData("ln secrets/k notes.txt")]
    [InlineData("rsync -a a/ secrets")]
    [InlineData("tar -xzf a.tgz -C secrets")]
    [InlineData("dd if=/dev/zero of=secrets/k")]
    [InlineData("curl -o secrets/k https://example.com")]
    [InlineData("sort -o secrets/k list")]
    [InlineData("parallel rm ::: secrets/k")]
    [InlineData("watch rm secrets/k")]
    [InlineData("cat <<'EOF' | grep x\nrm secrets/k\nEOF")]
    public void CommandsWhoseWritesAreNotParsedShowNone(string command)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.DoesNotContain(writes.Files, file => file.StartsWith("secrets", StringComparison.Ordinal));
        Assert.DoesNotContain(writes.Trees, tree => tree.StartsWith("secrets", StringComparison.Ordinal));
    }

    [Fact]
    public void ARelativePathAfterACdIsTakenAsWritten()
    {
        ShellWrites writes = ShellWriteTargets.Find("cd tests && echo x > T.cs", ShellDialect.Posix);

        Assert.Equal(["T.cs"], writes.Files);
    }

    [Fact]
    public void ASubshellTargetCarriesNoParenthesis()
    {
        ShellWrites writes = ShellWriteTargets.Find("(echo x > tests/T.cs)", ShellDialect.Posix);

        Assert.Equal(["tests/T.cs"], writes.Files);
    }

    [Fact]
    public void ACommandAroundASubstitutionKeepsItsOtherOperands() =>
        Assert.Contains("secrets/k", ShellWriteTargets.Find("rm $(cat list) secrets/k", ShellDialect.Posix).Files);

    [Theory]
    [InlineData("bash -c \"lockflight approve spec\"", "approve spec")]
    [InlineData("eval \"dotnet lockflight baseline build\"", "baseline build")]
    [InlineData("sh -c 'cd /x && lockflight redgreen reset'", "redgreen reset")]
    [InlineData("pwsh -Command \"lockflight update\"", "update")]
    public void AReservedCommandInsideANestedCommandStringIsFound(string command, string expected) =>
        Assert.Equal(expected, ShellWriteTargets.Find(command, ShellDialect.Posix).HumanOnly);
}
