using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Tests.Ledger;

public sealed class CallRedactionTests
{
    [Theory]
    [InlineData("curl -H \"Authorization: Bearer abc.def\" https://x", "curl -H \"Authorization: Bearer *** https://x")]
    [InlineData("export TOKEN=s3cr3t && run", "export TOKEN=*** && run")]
    [InlineData("gh api -H x ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "gh api -H x ***")]
    [InlineData("grep -l Foo web | xargs sed -i 's/a/b/'", "grep -l Foo web | xargs sed -i 's/a/b/'")]
    public void CredentialsAreMaskedAndOrdinaryCommandsKept(string call, string expected) => Assert.Equal(expected, CallRedaction.Shorten(call));

    [Fact]
    public void ALongCallIsShortenedToOneLine()
    {
        string shortened = CallRedaction.Shorten("echo a\n" + new string('x', 10).Replace("x", "word ", StringComparison.Ordinal) + string.Concat(Enumerable.Repeat("echo b; ", 40)));

        Assert.DoesNotContain('\n', shortened);
        Assert.Equal(CallRedaction.MaxLength + 1, shortened.Length);
        Assert.EndsWith("…", shortened, StringComparison.Ordinal);
    }
}
