using System.Text;
using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class CiArtifactReaderTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-ci-artifact-");

    [Fact]
    public void AMissingFileIsNeitherADocumentNorAProblem()
    {
        CiArtifactReading reading = CiArtifactReader.Read(Path.Combine(directory.FullName, "evidence.json"));

        Assert.Null(reading.Document);
        Assert.Null(reading.Problem);
    }

    [Fact]
    public void AnObjectWithinTheLimitsIsRead()
    {
        string path = Write("""{ "schema": "lockflight.ci-review/v1", "nested": { "list": [1, 2, { "deep": true }] } }""");

        CiArtifactReading reading = CiArtifactReader.Read(path);

        Assert.Null(reading.Problem);
        Assert.Equal("lockflight.ci-review/v1", reading.Document!["schema"]!.GetValue<string>());
        Assert.True(reading.Document["nested"]!["list"]![2]!["deep"]!.GetValue<bool>());
    }

    [Fact]
    public void AByteOrderMarkIsAllowed()
    {
        string path = Path.Combine(directory.FullName, "bom.json");
        File.WriteAllText(path, """{ "a": 1 }""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(1, CiArtifactReader.Read(path).Document!["a"]!.GetValue<int>());
    }

    [Fact]
    public void AFileOverTheSizeLimitIsRefusedBeforeItIsParsed()
    {
        string path = Path.Combine(directory.FullName, "large.json");
        using (FileStream stream = File.Create(path))
        {
            // Zero bytes are not JSON: a size problem rather than a syntax one shows nothing was parsed.
            stream.SetLength(CiArtifactReader.MaxBytes + 1L);
        }

        CiArtifactReading reading = CiArtifactReader.Read(path);

        Assert.Null(reading.Document);
        Assert.Equal($"it is {CiArtifactReader.MaxBytes + 1L} bytes, more than the {CiArtifactReader.MaxBytes} the report reads", reading.Problem);
    }

    [Fact]
    public void AFileAtTheSizeLimitIsParsed()
    {
        string body = """{ "padding": "" }""";
        string path = Write(body.Replace("\"\"", $"\"{new string('x', CiArtifactReader.MaxBytes - body.Length)}\"", StringComparison.Ordinal));

        Assert.Null(CiArtifactReader.Read(path).Problem);
    }

    [Fact]
    public void NestingDeeperThanTheLimitIsRefused()
    {
        string path = Write(Nested(CiArtifactReader.MaxDepth + 1));

        CiArtifactReading reading = CiArtifactReader.Read(path);

        Assert.Null(reading.Document);
        Assert.Equal($"it nests objects and arrays deeper than {CiArtifactReader.MaxDepth} levels", reading.Problem);
    }

    [Fact]
    public void NestingDeeperThanTheReadersOwnDefaultIsRefusedByNameNotAsASyntaxError()
    {
        CiArtifactReading reading = CiArtifactReader.Read(Write(Nested(10_000)));

        Assert.Equal($"it nests objects and arrays deeper than {CiArtifactReader.MaxDepth} levels", reading.Problem);
    }

    [Fact]
    public void NestingAtTheLimitIsRead() =>
        Assert.Null(CiArtifactReader.Read(Write(Nested(CiArtifactReader.MaxDepth))).Problem);

    [Fact]
    public void APropertyNamedTwiceIsRefusedWithItsPath()
    {
        string path = Write("""{ "head": "a", "review": { "verdict": "FAIL", "verdict": "PASS" } }""");

        CiArtifactReading reading = CiArtifactReader.Read(path);

        Assert.Null(reading.Document);
        Assert.Equal("the property 'review.verdict' appears twice in one object", reading.Problem);
    }

    [Fact]
    public void ATopLevelPropertyNamedTwiceIsRefused() =>
        Assert.Equal("the property 'verdict' appears twice in one object", CiArtifactReader.Read(Write("""{ "verdict": "FAIL", "verdict": "PASS" }""")).Problem);

    [Fact]
    public void TextThatIsNotJsonIsRefused() =>
        Assert.StartsWith("it is not valid JSON (", CiArtifactReader.Read(Write("{ \"truncated\": ")).Problem, StringComparison.Ordinal);

    [Fact]
    public void AnEmptyFileIsRefused() =>
        Assert.StartsWith("it is not valid JSON (", CiArtifactReader.Read(Write(string.Empty)).Problem, StringComparison.Ordinal);

    [Fact]
    public void JsonThatIsNotAnObjectIsRefused() =>
        Assert.Equal("it is not a JSON object", CiArtifactReader.Read(Write("[1, 2]")).Problem);

    [Fact]
    public void TheDocumentReadCanBeChanged()
    {
        JsonObject document = CiArtifactReader.Read(Write("""{ "a": { "b": 1 } }""")).Document!;

        document["c"] = 2;
        document["a"]!["b"] = 3;

        Assert.Equal("""{"a":{"b":3},"c":2}""", document.ToJsonString());
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private static string Nested(int levels) => string.Concat(Enumerable.Repeat("{\"a\":", levels - 1)) + "{}" + new string('}', levels - 1);

    private string Write(string text)
    {
        string path = Path.Combine(directory.FullName, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, text);
        return path;
    }
}
