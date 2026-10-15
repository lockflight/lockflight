using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Lifecycle.Approvals;

public sealed class ArtifactHashTests
{
    [Theory]
    [InlineData("# Spec\n- **FR-001**: A\n")]
    [InlineData("# Spec\r\n- **FR-001**: Café\r\n")]
    [InlineData("﻿# Spec\r\n- **FR-001**: A\n")]
    [InlineData("old Mac\rline endings\r")]
    [InlineData("")]
    public void UtfEightTextHashesAsEarlierEnginesRecordedIt(string text) =>
        Assert.Equal(EarlierHash(text), ArtifactHash.Of(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ABomAndWindowsLineEndingsDoNotChangeTheHash() =>
        Assert.Equal(
            ArtifactHash.Of(Encoding.UTF8.GetBytes("a\nb\n")),
            ArtifactHash.Of([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("a\r\nb\r\n")]));

    [Theory]
    [InlineData("a\u2028b")]
    [InlineData("a\u0085b")]
    [InlineData("a\u000Cb")]
    public void UnicodeSeparatorsAreNotLineEndings(string text) =>
        Assert.NotEqual(ArtifactHash.Of(Encoding.UTF8.GetBytes("a\nb")), ArtifactHash.Of(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void DifferentInvalidUtfEightBytesHashDifferently() =>
        Assert.NotEqual(ArtifactHash.Of([(byte)'a', 0xFF]), ArtifactHash.Of([(byte)'a', 0xFE]));

    [Theory]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    [InlineData("utf-32BE")]
    [InlineData("utf-8-twice")]
    public void FilesEarlierEnginesDecodedHashAsTheyDid(string encodingName)
    {
        const string Text = "# Spec\r\n- **FR-001**: Café\n";
        byte[] bytes = encodingName == "utf-8-twice"
            ? [0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Text)]
            : [.. Encoding.GetEncoding(encodingName).GetPreamble(), .. Encoding.GetEncoding(encodingName).GetBytes(Text)];
        string path = Path.Combine(Path.GetTempPath(), $"lockflight-hash-{Guid.CreateVersion7()}.md");
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.Equal(EarlierFileHash(path), ArtifactHash.Of(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string EarlierFileHash(string path) => EarlierHash(File.ReadAllText(path));

    private static string EarlierHash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.TrimStart('﻿').ReplaceLineEndings("\n"))));
}
