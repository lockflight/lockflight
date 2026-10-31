using System.Text;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class GitOutputParsersTests
{
    private const string OldId = "1111111111111111111111111111111111111111";
    private const string NewId = "2222222222222222222222222222222222222222";
    private const string Zero = "0000000000000000000000000000000000000000";

    [Fact]
    public void RawRecordsKeepModesBlobsStatusAndUnusualNames()
    {
        string raw = $":100644 100644 {OldId} {NewId} M\0src/a b.cs\0:000000 100644 {Zero} {NewId} A\0new\tfile.cs\0:120000 000000 {OldId} {Zero} D\0link\0";

        IReadOnlyList<RawDiffEntry> entries = RawDiff.Parse(raw);

        Assert.Equal(
            [
                new RawDiffEntry("100644", "100644", OldId, NewId, 'M', "src/a b.cs"),
                new RawDiffEntry("000000", "100644", Zero, NewId, 'A', "new\tfile.cs"),
                new RawDiffEntry("120000", "000000", OldId, Zero, 'D', "link"),
            ],
            entries);
    }

    [Fact]
    public void NumstatNamesOnlyTheBinaryPaths() =>
        Assert.Equal(["logo.png", "with\ttab.bin"], NumstatDiff.BinaryPaths("3\t1\tsrc/a.cs\0-\t-\tlogo.png\0-\t-\twith\ttab.bin\00\t0\tempty.txt\0").Order(StringComparer.Ordinal));

    [Fact]
    public void CatFileBatchKeepsBlobsByteForByteAndSkipsMissingObjects()
    {
        byte[] content = [.. "a\nb\0c"u8];
        byte[] output = [.. Encoding.UTF8.GetBytes($"{OldId} blob {content.Length}\n"), .. content, (byte)'\n', .. "HEAD:gone missing\n"u8,
            .. Encoding.UTF8.GetBytes($"{NewId} tree 0\n\n"), .. Encoding.UTF8.GetBytes($"{OldId} blob 2\n"), .. "ok"u8, (byte)'\n'];

        IReadOnlyDictionary<string, byte[]> blobs = CatFileBatch.Parse(output, ["first", "gone", "dir", "last"]);

        Assert.Equal(["first", "last"], blobs.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(content, blobs["first"]);
        Assert.Equal("ok"u8.ToArray(), blobs["last"]);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n', (byte)'b' }, "a\nb")]
    [InlineData(new byte[] { (byte)'a', 0, (byte)'b' }, null)]
    public void RevisionTextDropsTheByteOrderMarkAndCarriageReturnsAndRefusesBinary(byte[] content, string? expected) =>
        Assert.Equal(expected, RevisionText.Decode(content));

    [Fact]
    public void WhitespacePairsDropOnlyReformattedLines()
    {
        RoutedLine[] removed = [new(1, "    return a+b;", "    return a+b;"), new(2, "\tint x;", "\tint x;")];
        RoutedLine[] added = [new(1, "  int   x;", "  int   x;"), new(2, "return a + b;", "return a + b;")];

        (IReadOnlyList<RoutedLine> keptAdded, IReadOnlyList<RoutedLine> keptRemoved) = WhitespacePairs.Drop(added, removed);

        Assert.Equal(["return a + b;"], keptAdded.Select(line => line.Text));
        Assert.Equal(["    return a+b;"], keptRemoved.Select(line => line.Text));
    }

    [Fact]
    public void WhitespacePairsKeepLinesThatSwapPlaces()
    {
        RoutedLine[] removed = [new(2, "    return Allow();", "    return Allow();"), new(3, "    return Deny();", "    return Deny();")];
        RoutedLine[] added = [new(2, "    return Deny();", "    return Deny();"), new(3, "    return Allow();", "    return Allow();")];

        (IReadOnlyList<RoutedLine> keptAdded, IReadOnlyList<RoutedLine> keptRemoved) = WhitespacePairs.Drop(added, removed);

        Assert.Single(keptAdded);
        Assert.Single(keptRemoved);
    }

    [Fact]
    public void WhitespacePairsKeepALineDeletedInOneHunkAndAddedBackInAnother()
    {
        // git diff -U0 of "A\nB\nC" -> "B\nA\nC": @@ -1 +0,0 @@ -A and @@ -2,0 +2 @@ +A.
        RoutedLine[] removed = [new(1, "app.UseAuthentication();", "app.UseAuthentication();")];
        RoutedLine[] added = [new(2, "app.UseAuthentication();", "app.UseAuthentication();")];

        (IReadOnlyList<RoutedLine> keptAdded, IReadOnlyList<RoutedLine> keptRemoved) = WhitespacePairs.Drop(added, removed);

        Assert.Single(keptAdded);
        Assert.Single(keptRemoved);
    }

    [Fact]
    public void WhitespacePairsPairInsideAHunkWithUnequalCounts()
    {
        RoutedLine[] removed = [new(4, "  a();", "  a();"), new(5, "  b();", "  b();"), new(9, "  c();", "  c();")];
        RoutedLine[] added = [new(4, "\ta();", "\ta();"), new(8, "\tc();", "\tc();")];

        (IReadOnlyList<RoutedLine> keptAdded, IReadOnlyList<RoutedLine> keptRemoved) = WhitespacePairs.Drop(added, removed);

        Assert.Empty(keptAdded);
        Assert.Equal(["  b();"], keptRemoved.Select(line => line.Text));
    }

    [Fact]
    public void WhitespacePairsDropAWholeReindentInOrder()
    {
        string[] lines = ["{", "    a();", "}", "{", "    b();", "}"];
        RoutedLine[] removed = [.. lines.Select((text, index) => new RoutedLine(index + 1, text, text))];
        RoutedLine[] added = [.. lines.Select((text, index) => new RoutedLine(index + 1, "\t" + text.Trim(), "\t" + text.Trim()))];

        (IReadOnlyList<RoutedLine> keptAdded, IReadOnlyList<RoutedLine> keptRemoved) = WhitespacePairs.Drop(added, removed);

        Assert.Empty(keptAdded);
        Assert.Empty(keptRemoved);
    }

    [Theory]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17\" /></ItemGroup></Project>", true)]
    [InlineData("<PackageReference Include=\"NUnit\" Version=\"4\" />", true)]
    [InlineData("<PackageReference Include=\"MSTest\" Version=\"3\" />", true)]
    [InlineData("<PackageReference Include=\"xunit.v3.mtp-v2\" Version=\"3\" />", true)]
    [InlineData("<Project Sdk=\"MSTest.Sdk/3.6.0\" />", true)]
    [InlineData("<IsTestProject>\n  true\n</IsTestProject>", true)]
    [InlineData("<PackageReference Include=\"xunit.assert\" Version=\"2\" />", false)]
    [InlineData("<PackageReference Include=\"NUnit.Analyzers\" Version=\"4\" />", false)]
    [InlineData("<!-- <PackageReference Include=\"xunit\" /> -->", false)]
    [InlineData("<IsTestProject>false</IsTestProject>", false)]
    public void TestProjectsAreRecognisedFromTheirText(string text, bool isTest) => Assert.Equal(isTest, ProjectText.MakesTestProject(text));
}
