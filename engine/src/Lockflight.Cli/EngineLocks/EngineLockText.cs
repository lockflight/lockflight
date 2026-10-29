using System.Text;
using System.Text.Json;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The lock's canonical text: two-space indent, keys in a fixed order, one platform per line, LF line endings and a final
/// newline, so a diff of an engine upgrade is one line per platform and a line-oriented reader can find a hash.
/// </summary>
internal static class EngineLockText
{
    public static string Write(EngineLock engineLock)
    {
        StringBuilder text = new();
        text.Append("{\n");
        text.Append($"  \"schema\": {Quoted(EngineLock.Schema)},\n");
        text.Append($"  \"package\": {Quoted(EngineLock.Package)},\n");
        text.Append($"  \"version\": {Quoted(engineLock.Version)},\n");
        text.Append($"  \"channel\": {Quoted(EngineLockNames.ToName(engineLock.Channel))},\n");
        text.Append($"  \"installScript\": {{ \"sha256\": {Quoted(engineLock.InstallScriptSha256)} }},\n");
        text.Append("  \"binaries\": {\n");
        string[] rids = [.. EngineRids.Released.Where(engineLock.Binaries.ContainsKey)];
        for (int index = 0; index < rids.Length; index++)
        {
            string separator = index < rids.Length - 1 ? "," : string.Empty;
            text.Append($"    {Quoted(rids[index])}: {{ \"sha256\": {Quoted(engineLock.Binaries[rids[index]])} }}{separator}\n");
        }

        text.Append("  }\n");
        text.Append("}\n");
        return text.ToString();
    }

    private static string Quoted(string value) => $"\"{JsonEncodedText.Encode(value)}\"";
}
