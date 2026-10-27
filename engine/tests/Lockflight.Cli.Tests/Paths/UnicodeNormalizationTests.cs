using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Paths;

public sealed class UnicodeNormalizationTests
{
    [Theory]
    [InlineData("cafe\u0301.txt", "caf\u00e9.txt")]
    [InlineData("caf\u00e9.txt", "caf\u00e9.txt")]
    [InlineData("\u0456\u0308", "\u0457")]
    [InlineData("\u0438\u0306", "\u0439")]
    [InlineData("\u304b\u3099", "\u304c")]
    [InlineData("\u1100\u1161\u11a8", "\uac01")]
    [InlineData("A\u030a", "\u00c5")]
    [InlineData("e\u0323\u0302", "\u1ec7")]
    [InlineData("e\u0302\u0323", "\u1ec7")]
    [InlineData("q\u0307\u0323", "q\u0323\u0307")]
    [InlineData("\u1ec7", "\u1ec7")]
    [InlineData("plain/ascii.txt", "plain/ascii.txt")]
    public void TextIsComposedToItsCanonicalForm(string text, string expected) =>
        Assert.Equal(expected, UnicodeNormalization.Compose(text));

    [Fact]
    public void ACombiningMarkWithNothingBeforeItIsKept() =>
        Assert.Equal("\u0301x", UnicodeNormalization.Compose("\u0301x"));
}
