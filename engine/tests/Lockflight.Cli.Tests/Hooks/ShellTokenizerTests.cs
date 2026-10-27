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
    [InlineData("rm a \\\nb", "rm|a|b")]
    [InlineData("echo \"C:\\repo\"", "echo|C:\\repo")]
    [InlineData("echo \"a$(rm x)b\"", "echo|(|rm|x|)|a$__b")]
    public void PosixCommandsSplitIntoWordsAndOperators(string command, string expected) =>
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
