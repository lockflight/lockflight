using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellTokenizerTests
{
    [Theory]
    [InlineData("a\nb", "a|;|b")]
    [InlineData("a\r\nb", "a|;|b")]
    [InlineData("echo x 2>&1 | tee", "echo|x|;|tee")]
    [InlineData("echo '>' x", "echo|>|x")]
    [InlineData("echo x >> y", "echo|x|W|y")]
    [InlineData("cat < in > out", "cat|R|in|W|out")]
    [InlineData("cat <<EOF\nrm x\nEOF\nls", "cat|R|H[rm x\n]|;|ls")]
    [InlineData("cat <<-'END'\n\trm x\n\tEND\nls", "cat|R|H[rm x\n]|;|ls")]
    [InlineData("cat <<A <<B\na1\nA\nb1\nB\nls", "cat|R|H[a1\n]|R|H[b1\n]|;|ls")]
    [InlineData("cat <<< 'a b' > out", "cat|H[a b]|W|out")]
    [InlineData("echo a # rm x\nls", "echo|a|;|ls")]
    [InlineData("echo ''#a; ls", "echo|#a|;|ls")]
    [InlineData("echo \"\"# rm x", "echo|#|rm|x")]
    [InlineData("rm a \\\nb", "rm|a|b")]
    [InlineData("echo \"C:\\repo\"", "echo|C:\\repo")]
    [InlineData("echo \"a$(rm x)b\"", "echo|(|rm|x|)|a$__b")]
    public void PosixCommandsSplitIntoWordsAndOperators(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("echo hi >f <<EOF \\\nEOF\nrm -rf .lockflight\nEOF", "echo|hi|W|f|R|H[rm -rf .lockflight\n]|EOF|;")]
    [InlineData("cat >f <<EOF <<<'\nEOF\n'\nrm -rf .lockflight\nEOF", "cat|W|f|R|H[rm -rf .lockflight\n]|H[\nEOF\n]|;")]
    [InlineData("cat <<EOF 'a\nb'\nbody\nEOF\nls", "cat|R|H[body\n]|a\nb|;|ls")]
    [InlineData("cat <<A \\\n<<B\na1\nA\nb1\nB\nls", "cat|R|H[a1\n]|R|H[b1\n]|;|ls")]
    [InlineData("cat <<EOF $(\nrm x\n)\nbody\nEOF", "cat|R|H[body\n]|(|;|rm|x|;|)|$__|;")]
    [InlineData("cat <<EOF `\nrm x\n`\nbody\nEOF", "cat|R|H[body\n]|(|;|rm|x|;|)|$__|;")]
    [InlineData("echo $(cat <<EOF)\nrm x\nEOF", "echo|(|cat|R|H[]|)|$__|;|rm|x|;|EOF")]
    [InlineData("echo $(cat <<A\nx\nA\n) <<B\nrm x\nB\nls", "echo|(|cat|R|H[x\n]|;|)|$__|R|H[rm x\n]|;|ls")]
    [InlineData("cat <<EOF; (\nrm x\n)\nEOF\nls", "cat|R|H[rm x\n)\n]|;|(|;|ls|)")]
    [InlineData("cat <<\nrm x", "cat|R|;|rm|x")]
    public void AHereDocumentBodyStartsAfterTheLogicalLineThatOpensIt(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("cat >f <<'' x\nhello\n\nrm -rf .lockflight\nx", "cat|W|f|R|H[hello\n]|x|;|rm|-rf|.lockflight|;|x")]
    [InlineData("cat <<\"\"\nhello\n\nls", "cat|R|H[hello\n]|;|ls")]
    [InlineData("cat <<<'' rm -rf .lockflight", "cat|H[]|rm|-rf|.lockflight")]
    [InlineData("<<<'' rm -rf .lockflight", "H[]|rm|-rf|.lockflight")]
    [InlineData("exec -a '' rm x", "exec|-a||rm|x")]
    [InlineData("echo \"\" $'' x", "echo|||x")]
    [InlineData("rm a \\\n b", "rm|a|b")]
    public void AnEmptyQuotedWordIsAWord(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("cat <<EOF\nE\\\nOF\nrm x\nEOF", "cat|R|H[]|;|rm|x|;|EOF")]
    [InlineData("cat <<E\\\nOF\nE\\\nOF\nrm x\nEOF", "cat|R|H[]|;|rm|x|;|EOF")]
    [InlineData("cat <<EOF\na\\\\\nEOF\nrm x", "cat|R|H[a\\\\\n]|;|rm|x")]
    [InlineData("cat <<'EOF'\nE\\\nOF\nrm x\nEOF", "cat|R|H[E\\\nOF\nrm x\n]|;")]
    [InlineData("cat <<-EOF\n\tE\\\n\tOF\nrm x\nEOF", "cat|R|H[E\tOF\nrm x\n]|;")]
    [InlineData("cat <<EOF\nx\\\r\nEOF\nrm x\nEOF", "cat|R|H[x\\\n]|;|rm|x|;|EOF")]
    public void AnUnquotedDelimiterJoinsABodyLineEndingInABackslashToTheNext(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("cat <<EOF\n$(rm x)\nEOF", "cat|R|H[$(rm x)\n]|;|(|rm|x|)")]
    [InlineData("cat <<EOF\n`rm x`\nEOF", "cat|R|H[`rm x`\n]|;|(|rm|x|)")]
    [InlineData("cat <<EOF\nit's \"${a:-$(rm x)}\"\nEOF\nls", "cat|R|H[it's \"${a:-$(rm x)}\"\n]|;|(|rm|x|)|ls")]
    [InlineData("cat <<EOF\n\\$(rm x)\nEOF", "cat|R|H[\\$(rm x)\n]|;")]
    [InlineData("cat <<'EOF'\n$(rm x)\nEOF", "cat|R|H[$(rm x)\n]|;")]
    public void AnUnquotedHereDocumentBodyRunsItsCommandSubstitutions(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("cat <<<$(rm x)", "cat|(|rm|x|)|H[$__]")]
    [InlineData("cat <<<`rm x` y", "cat|(|rm|x|)|H[$__]|y")]
    [InlineData("cat <<(rm x)", "cat|R|(|rm|x|)|$__")]
    [InlineData("cat <<$\"EOF\"\nbody\nEOF\nrm x\n$EOF", "cat|R|H[body\n]|;|rm|x|;|$EOF")]
    [InlineData("cat <<$\"EOF\"\nbody\n$EOF\nrm x\nEOF", "cat|R|H[body\n]|;|rm|x|;|EOF")]
    [InlineData("rm $\"x\"", "rm|x")]
    [InlineData("echo hi \\\r\nrm x", "echo|hi|\r|;|rm|x")]
    public void AWordIsReadAsBashOrZshReadsIt(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.Posix)));

    [Theory]
    [InlineData("S=/x", null, false)]
    [InlineData("S=\"/x\"", 2, false)]
    [InlineData("'S'=/x", 0, false)]
    [InlineData("S=$X", null, false)]
    [InlineData("S=\"$X\"", 2, false)]
    [InlineData("S='$X'", 2, true)]
    [InlineData("S=\\$X", 2, true)]
    [InlineData("S=$'\\x24X'", 2, true)]
    [InlineData("S=$(echo '$X')", null, false)]
    public void AWordRecordsWhereQuotingStartsAndWhetherADollarIsLiteral(string command, int? quotedFrom, bool literalDollar)
    {
        ShellToken word = ShellTokenizer.Tokenize(command, ShellDialect.Posix).Last();

        Assert.Equal(quotedFrom, word.QuotedFrom);
        Assert.Equal(literalDollar, word.LiteralDollar);
    }

    [Theory]
    [InlineData("Set-Content -Path C:\\a\\b -Value x", "Set-Content|-Path|C:\\a\\b|-Value|x")]
    [InlineData("& { Remove-Item x }", ";|;|Remove-Item|x|;")]
    [InlineData("$t = @'\nRemove-Item x\n'@\nls", "$t|=|Remove-Item x|;|ls")]
    [InlineData("<# Remove-Item x\n#> ls", "ls")]
    [InlineData("Write-Host ${env:TEMP}", "Write-Host|${env:TEMP}")]
    [InlineData("dir *> out.txt", "dir|W|out.txt")]
    public void PowerShellCommandsSplitIntoWordsAndOperators(string command, string expected) =>
        Assert.Equal(expected, Describe(ShellTokenizer.Tokenize(command, ShellDialect.PowerShell)));

    private static string Describe(List<ShellToken> tokens) => string.Join('|', tokens.Select(token => token.Kind switch
    {
        ShellTokenKind.Word => token.Text,
        ShellTokenKind.Separator => ";",
        ShellTokenKind.WriteRedirect => "W",
        ShellTokenKind.InputRedirect => "R",
        ShellTokenKind.Close => ")",
        ShellTokenKind.HereText => $"H[{token.Text}]",
        _ => "(",
    }));
}
