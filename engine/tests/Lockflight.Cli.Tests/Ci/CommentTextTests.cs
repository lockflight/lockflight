using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CommentTextTests
{
    [Theory]
    [InlineData("see https://example.test/login", "see https:​//example.test/login")]
    [InlineData("see WWW.example.test", "see WWW​.example.test")]
    [InlineData("ShowsWww.Prefix", "ShowsWww.Prefix")]
    public void PlainTextKeepsNoBareLink(string text, string expected) => Assert.Equal(expected, CommentText.Plain(text));
}
