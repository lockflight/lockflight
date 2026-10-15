using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// How the engine writes JSON files: indented, ending with a newline, and without escaping non-ASCII text or characters such as
/// <c>&amp;</c> and <c>+</c> — the files are read by people and git diffs, never embedded in HTML.
/// </summary>
internal static class JsonFiles
{
    public static JsonSerializerOptions Indented { get; } = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Format(JsonNode document) => document.ToJsonString(Indented) + "\n";

    /// <summary>Writes the document, creating its directory first.</summary>
    public static void Write(string path, JsonNode document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Format(document));
    }
}
