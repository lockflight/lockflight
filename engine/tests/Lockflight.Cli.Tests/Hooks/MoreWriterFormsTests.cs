using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class MoreWriterFormsTests
{
    [Theory]
    [InlineData("for f in src/a.cs secrets/k; do echo x > $f; done", "secrets/k")]
    [InlineData("for f in a secrets/k\ndo\n  rm \"$f\"\ndone", "secrets/k")]
    [InlineData("for name in k j; do touch secrets/${name}; done", "secrets/k")]
    [InlineData("for f in secrets/{a,k}; do rm $f; done", "secrets/k")]
    [InlineData("for f in .lockflight/*; do touch $f; done", ".lockflight/*")]
    public void ALoopVariableStandsForEachWordOfItsList(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Fact]
    public void ALoopOverACommandsOutputHasHiddenTargets() =>
        Assert.NotNull(ShellWriteTargets.Find("for f in $(ls secrets); do rm $f; done", ShellDialect.Posix).Hidden);

    [Theory]
    [InlineData("rsync -a --remove-source-files secrets/ /tmp/s", "secrets/")]
    [InlineData("cp -al secrets /tmp/copy", "secrets")]
    public void SourcesThatAreMovedOrLinkedAsATreeAreTrees(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.Posix).Trees);

    [Theory]
    [InlineData("ln lockflight.yaml hl", "lockflight.yaml")]
    [InlineData("ln -f secrets/k /tmp/k", "secrets/k")]
    [InlineData("ln -t /tmp secrets/k", "secrets/k")]
    [InlineData("cp --link lockflight.yaml hl", "lockflight.yaml")]
    [InlineData("cp -l secrets/k /tmp/k", "secrets/k")]
    public void AHardLinkCountsItsSourceAsWritten(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("ln -s secrets/k /tmp/k")]
    [InlineData("ln -sf ../secrets/k link")]
    [InlineData("cp secrets/k /tmp/k")]
    public void ASymbolicLinkOrACopyLeavesItsSourceAlone(string command) =>
        Assert.DoesNotContain("secrets/k", ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("sort -o secrets/k list.txt", "secrets/k")]
    [InlineData("sort -u --output=secrets/k list.txt", "secrets/k")]
    [InlineData("sort -osecrets/k list.txt", "secrets/k")]
    [InlineData("uniq list.txt secrets/k", "secrets/k")]
    [InlineData("uniq -c -f 1 list.txt secrets/k", "secrets/k")]
    [InlineData("awk -i inplace '{print}' secrets/k", "secrets/k")]
    [InlineData("gawk -i inplace -v n=1 -f fix.awk secrets/k", "secrets/k")]
    [InlineData("yq -i '.a = 1' secrets/k.yaml", "secrets/k.yaml")]
    [InlineData("yq --inplace -P '.a = 1' secrets/k.yaml", "secrets/k.yaml")]
    [InlineData("git checkout-index -f -- secrets/k", "secrets/k")]
    [InlineData("echo x > secrets/k{1..3}", "secrets/k2")]
    [InlineData("touch secrets/{a..c}.txt", "secrets/b.txt")]
    [InlineData("rm secrets/k{01..10..3}", "secrets/k07")]
    [InlineData("function f { rm secrets/k; }; f", "secrets/k")]
    [InlineData("function f() { echo x > secrets/k; }", "secrets/k")]
    [InlineData("setsid rm secrets/k", "secrets/k")]
    [InlineData("setsid -f rm secrets/k", "secrets/k")]
    [InlineData("flock /tmp/lock rm secrets/k", "secrets/k")]
    [InlineData("flock -w 5 /tmp/lock rm secrets/k", "secrets/k")]
    [InlineData("flock /tmp/lock -c 'rm secrets/k'", "secrets/k")]
    [InlineData("caffeinate -i -t 60 rm secrets/k", "secrets/k")]
    [InlineData("arch -arm64 rm secrets/k", "secrets/k")]
    [InlineData("arch -arch x86_64 rm secrets/k", "secrets/k")]
    [InlineData("env -S 'rm secrets/k'", "secrets/k")]
    [InlineData("env -S\"rm -f\" secrets/k", "secrets/k")]
    [InlineData("env --split-string='rm secrets/k'", "secrets/k")]
    [InlineData("ionice -c 3 rm secrets/k", "secrets/k")]
    [InlineData("chronic rm secrets/k", "secrets/k")]
    public void MoreWritersAndRunnersAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("git checkout-index -a -f", ".")]
    [InlineData("git checkout-index -a --prefix=out/", "out")]
    public void ACheckoutOfTheWholeIndexRewritesATree(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.Posix).Trees);

    [Theory]
    [InlineData("sort list.txt")]
    [InlineData("uniq list.txt")]
    [InlineData("awk '{print}' secrets/k")]
    [InlineData("yq '.a' secrets/k.yaml")]
    [InlineData("echo {1..3}")]
    [InlineData("flock /tmp/lock true")]
    [InlineData("ls | xargs sort")]
    [InlineData("find . -name '*.txt' -exec awk '{print}' {} \\;")]
    [InlineData("ls | while read f; do sort \"$f\"; done")]
    public void ReadingFormsWriteNothing(string command)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.Null(writes.Hidden);
        Assert.Empty(writes.Files);
        Assert.Empty(writes.Trees);
    }

    [Fact]
    public void AHugeBraceSequenceIsHidden() =>
        Assert.NotNull(ShellWriteTargets.Find("rm secrets/k{1..100000}", ShellDialect.Posix).Hidden);

    [Theory]
    [InlineData("Invoke-WebRequest https://example.com/x -OutFile secrets\\k", "secrets\\k")]
    [InlineData("iwr https://example.com/x -o secrets\\k", "secrets\\k")]
    [InlineData("Invoke-RestMethod -Uri https://example.com/x -OutFile:secrets\\k", "secrets\\k")]
    [InlineData("Remove-Item lockflight.yaml,src\\a.cs", "lockflight.yaml")]
    [InlineData("Remove-Item src\\a.cs, secrets\\k", "secrets\\k")]
    [InlineData("Set-Content -Path a.txt,secrets\\k -Value x", "secrets\\k")]
    [InlineData("Remove-Item FileSystem::C:\\repo\\secrets\\k", "C:\\repo\\secrets\\k")]
    [InlineData("Remove-Item Microsoft.PowerShell.Core\\FileSystem::secrets\\k", "secrets\\k")]
    [InlineData("xcopy C:\\tmp\\k secrets\\", "secrets\\")]
    public void MorePowerShellWritesAreFound(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Files);

    [Theory]
    [InlineData("Expand-Archive a.zip -DestinationPath secrets", "secrets")]
    [InlineData("Expand-Archive -Path a.zip secrets -Force", "secrets")]
    [InlineData("Expand-Archive a.zip", ".")]
    [InlineData("robocopy C:\\tmp\\conf secrets /E", "secrets")]
    [InlineData("robocopy C:\\tmp\\conf . /MIR", ".")]
    [InlineData("robocopy secrets C:\\tmp\\out /MOVE", "secrets")]
    [InlineData("xcopy C:\\tmp\\conf secrets /E /I", "secrets")]
    public void PowerShellExtractionsAndWindowsCopiesRewriteATree(string command, string expectedTree) =>
        Assert.Contains(expectedTree, ShellWriteTargets.Find(command, ShellDialect.PowerShell).Trees);
}
