using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellLiteralVariableTests
{
    [Theory]
    [InlineData("S=/tmp/out; cat > $S/dump.py", "/tmp/out/dump.py")]
    [InlineData("S=/tmp/out; echo x > ${S}/a", "/tmp/out/a")]
    [InlineData("S=\"/tmp/out\"; echo x > \"$S/a\"", "/tmp/out/a")]
    [InlineData("S='/tmp/out'\necho x > $S/a", "/tmp/out/a")]
    [InlineData("export S=/tmp/out; echo x > $S/a", "/tmp/out/a")]
    [InlineData("S=/tmp/out && cp a $S/b", "/tmp/out/b")]
    [InlineData("A=/tmp; B=$A/out; echo x > $B/f", "/tmp/out/f")]
    [InlineData("S=/tmp; S=/tmp/out; echo x > $S/a", "/tmp/out/a")]
    [InlineData("true && S=/tmp/out && echo x > $S/a", "/tmp/out/a")]
    [InlineData("S=/tmp/out; (echo x > $S/a)", "/tmp/out/a")]
    [InlineData("S=/tmp/out; bash -c \"echo x > $S/a\"", "/tmp/out/a")]
    [InlineData("S=/tmp/out; cd $S/async-uk && cat > dump.py <<'EOF'\nprint(1)\nEOF", "/tmp/out/async-uk/dump.py")]
    public void ALiteralVariableIsReplacedByItsValue(string command, string expectedTarget)
    {
        ShellWrites writes = ShellWriteTargets.Find(command, ShellDialect.Posix);

        Assert.Contains(expectedTarget, writes.Files);
        Assert.Empty(writes.FloatingFiles);
    }

    [Fact]
    public void AnArchivePipedIntoTarWritesOnlyTheExtractDirectory()
    {
        ShellWrites writes = ShellWriteTargets.Find("S=/tmp/out; git archive HEAD Directory.Build.props lockflight.yaml | tar -x -C $S", ShellDialect.Posix);

        Assert.Empty(writes.Files);
        Assert.Equal(["/tmp/out"], writes.Trees);
        Assert.Null(writes.Hidden);
    }

    [Theory]
    [InlineData("git archive -o lockflight.yaml HEAD", "lockflight.yaml")]
    [InlineData("git archive --output=out/a.tar HEAD src", "out/a.tar")]
    public void AGitArchiveOutputFileIsAWrite(string command, string expectedTarget) =>
        Assert.Contains(expectedTarget, ShellWriteTargets.Find(command, ShellDialect.Posix).Files);

    [Theory]
    [InlineData("echo x > $S/f")]
    [InlineData("S=C:\\\\proj; echo x > $S/f")]
    [InlineData("S=C:proj; echo x > $S/f")]
    [InlineData("S=$(mktemp -d); echo x > $S/f")]
    [InlineData("S=/tmp; S=$(pwd); echo x > $S/f")]
    [InlineData("S=/tmp; S=`pwd`; echo x > $S/f")]
    [InlineData("(S=/tmp); echo x > $S/f")]
    [InlineData("v=$(S=/tmp); echo x > $S/f")]
    [InlineData("echo x > $S/f; S=/tmp")]
    [InlineData("S=/tmp true; echo x > $S/f")]
    [InlineData("S=/tmp echo x > $S/f")]
    [InlineData("S=/tmp; S=/repo > $S/f")]
    [InlineData("echo x > ${S:-/tmp}/f")]
    [InlineData("S=/tmp; unset S; echo x > $S/f")]
    [InlineData("S=/tmp | true; echo x > $S/f")]
    [InlineData("true | S=/tmp; echo x > $S/f")]
    [InlineData("S=/tmp & echo x > $S/f")]
    [InlineData("false && S=/tmp; echo x > $S/f")]
    [InlineData("false || S=/tmp; echo x > $S/f")]
    [InlineData("false || S=/tmp && echo x > $S/f")]
    [InlineData("S=/tmp; f() { S=/repo; }; f; echo x > $S/f")]
    [InlineData("S=/tmp; if true; then S=/repo; fi; echo x > $S/f")]
    [InlineData("S=/tmp; read S; echo x > $S/f")]
    [InlineData("S=/tmp; eval S=/repo; echo x > $S/f")]
    [InlineData("S=/tmp; source env.sh; echo x > $S/f")]
    [InlineData("S=/tmp; printf -v S /repo; echo x > $S/f")]
    [InlineData("S=/tmp; sleep 1 & wait -p S; echo x > $S/f")]
    [InlineData("S=/tmp; export -n S; echo x > $S/f")]
    [InlineData("S=/tmp; declare S=/repo; echo x > $S/f")]
    [InlineData("S=/tmp; S=(/repo); echo x > $S/f")]
    [InlineData("S=/tmp; { S=/repo; } | cat; echo x > $S/f")]
    [InlineData("S=/tmp; : ${S::=/repo}; echo x > $S/f")]
    [InlineData("S=/tmp; echo $((S=1)); echo x > $S/f")]
    [InlineData("S=/tmp; ((S=1)); echo x > $S/f")]
    [InlineData("S=/tmp; S+=/x; echo x > $S/f")]
    [InlineData("S=/tmp; S[1]=/x; echo x > $S/f")]
    [InlineData("S=/tmp; IFS=/; echo x > $S/f")]
    [InlineData("S=/tmp; echo x > '$S'/f")]
    [InlineData("S=/tmp; echo x > \\$S/f")]
    [InlineData("'S=/tmp'; echo x > $S/f")]
    [InlineData("S=~/x; echo x > $S/f")]
    [InlineData("S=/tmp/*; echo x > $S/f")]
    [InlineData("S='/tmp/a b'; echo x > $S/f")]
    [InlineData("S=; echo x > $S/f")]
    [InlineData("S=/tmp; echo x > $S:h/f")]
    [InlineData("S=/tmp; echo x > $S[1]/f")]
    [InlineData("A=/old; A=/tmp B=$A; echo x > $B/f")]
    [InlineData("S=/tmp; bash -c 'echo x > $S/f'")]
    [InlineData("__=/tmp; echo x > $(pwd)/f")]
    public void AVariableThatIsNotKnownExactlyStaysUnresolved(string command)
    {
        IReadOnlyList<string> files = ShellWriteTargets.Find(command, ShellDialect.Posix).Files;

        Assert.DoesNotContain("/tmp/f", files);
        Assert.Contains(files, file => file.Contains('$', StringComparison.Ordinal));
    }

    [Fact]
    public void AnAssignmentAfterAndHoldsOnlyUntilTheListEnds()
    {
        IReadOnlyList<string> files = ShellWriteTargets.Find("true && S=/tmp && echo x > $S/a; echo x > $S/b", ShellDialect.Posix).Files;

        Assert.Contains("/tmp/a", files);
        Assert.Contains("$S/b", files);
    }

    [Fact]
    public void AVariableAfterAnUnknownCdIsResolvedButTheRelativeTargetStillFloats()
    {
        ShellWrites writes = ShellWriteTargets.Find("S=/tmp; cd $X && echo x > $S/a && echo x > b", ShellDialect.Posix);

        Assert.Contains("/tmp/a", writes.Files);
        Assert.Contains("b", writes.FloatingFiles);
    }
}
