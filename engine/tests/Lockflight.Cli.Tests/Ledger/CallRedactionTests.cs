using Lockflight.Cli.Ledger;

namespace Lockflight.Cli.Tests.Ledger;

public sealed class CallRedactionTests
{
    [Theory]
    [InlineData("curl -H \"Authorization: Bearer abc.def\" https://x", "curl -H \"Authorization: Bearer *** https://x")]
    [InlineData("export TOKEN=s3cr3t && run", "export TOKEN=*** && run")]
    [InlineData("gh api -H x ghp_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "gh api -H x ***")]
    [InlineData("grep -l Foo web | xargs sed -i 's/a/b/'", "grep -l Foo web | xargs sed -i 's/a/b/'")]
    [InlineData("mysql --password s3cr3t -h db", "mysql --password *** -h db")]
    [InlineData("mysql --password=s3cr3t -h db", "mysql --password=*** -h db")]
    [InlineData("deploy --api-token abc123 --env prod", "deploy --api-token *** --env prod")]
    [InlineData("vault login --client-secret x/y+z", "vault login --client-secret ***")]
    [InlineData("git clone https://user:hunter2@example.com/r.git", "git clone https://user:***@example.com/r.git")]
    [InlineData("curl https://:p%40ss@example.com/x", "curl https://:***@example.com/x")]
    [InlineData("aws configure set aws_secret_access_key wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", "aws configure set aws_secret_access_key ***")]
    [InlineData("echo wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY | aws configure", "echo *** | aws configure")]
    [InlineData("s=Zm9vYmFy+YmF6/cXV4+cXV1eA9kZXZpY2U= run", "s=*** run")]
    [InlineData("cat /home/user/Projects/MyApp2/src/Program.cs", "cat /home/user/Projects/MyApp2/src/Program.cs")]
    [InlineData("ls /home/user/lockflight-checkout/engine/tests/Lockflight.Cli.Tests/Hooks", "ls /home/user/lockflight-checkout/engine/tests/Lockflight.Cli.Tests/Hooks")]
    [InlineData("git log origin/main..feature/AddPaymentProviderV2", "git log origin/main..feature/AddPaymentProviderV2")]
    [InlineData("ssh-keygen -t ed25519 -f key.pem", "ssh-keygen -t ed25519 -f key.pem")]
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
