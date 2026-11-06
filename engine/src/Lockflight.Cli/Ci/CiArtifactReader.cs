using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.EngineLocks;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Reads a JSON artifact another CI job wrote (the judge's evidence, the review's result). Those jobs ran the pull request's code,
/// so the file is untrusted: it is refused before it is parsed when it is larger than <see cref="MaxBytes"/>, and refused when it is
/// nested deeper than <see cref="MaxDepth"/>, names a property twice in one object (readers disagree on which one counts) or is not a
/// JSON object. The report's memory is bounded by the size limit, never by the file.
/// </summary>
internal static class CiArtifactReader
{
    /// <summary>The largest artifact read, in bytes. The judge caps every free-text field, so real evidence is far smaller.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    /// <summary>The deepest nesting of objects and arrays read; the judge's evidence, its gate result included, nests far less.</summary>
    public const int MaxDepth = 32;

    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    public static CiArtifactReading Read(string path)
    {
        byte[] bytes;
        try
        {
            if (!File.Exists(path))
            {
                return CiArtifactReading.None;
            }

            using FileStream stream = File.OpenRead(path);
            if (stream.Length > MaxBytes)
            {
                return Refused($"it is {stream.Length} bytes, more than the {MaxBytes} the report reads");
            }

            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Refused($"it cannot be read ({exception.Message})");
        }

        ReadOnlyMemory<byte> content = bytes.AsSpan().StartsWith(ByteOrderMark) ? bytes.AsMemory(ByteOrderMark.Length) : bytes;
        if (TooDeep(content.Span, out string? syntaxProblem))
        {
            return Refused($"it nests objects and arrays deeper than {MaxDepth} levels");
        }

        if (syntaxProblem is not null)
        {
            return Refused($"it is not valid JSON ({syntaxProblem})");
        }

        JsonDocumentOptions options = new() { MaxDepth = MaxDepth };
        try
        {
            using JsonDocument document = JsonDocument.Parse(content, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Refused("it is not a JSON object");
            }

            if (JsonDuplicateKeys.Find(document.RootElement) is string duplicate)
            {
                return Refused($"the property '{duplicate}' appears twice in one object");
            }

            return new CiArtifactReading(JsonObject.Create(document.RootElement.Clone()), null);
        }
        catch (JsonException exception)
        {
            return Refused($"it is not valid JSON ({exception.Message})");
        }
    }

    /// <summary>
    /// Whether the nesting goes past <see cref="MaxDepth"/>, found in one forward pass before anything is built; a syntax error met
    /// first is returned in <paramref name="syntaxProblem"/>.
    /// </summary>
    private static bool TooDeep(ReadOnlySpan<byte> content, out string? syntaxProblem)
    {
        syntaxProblem = null;
        // One level more than allowed, so the reader reports the extra level to this check instead of throwing on it.
        Utf8JsonReader reader = new(content, new JsonReaderOptions { MaxDepth = MaxDepth + 1 });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth >= MaxDepth)
                {
                    return true;
                }
            }
        }
        catch (JsonException exception)
        {
            syntaxProblem = exception.Message;
        }

        return false;
    }

    private static CiArtifactReading Refused(string problem) => new(null, problem);
}
