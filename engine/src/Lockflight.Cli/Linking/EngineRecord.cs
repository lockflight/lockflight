using System.Security.Cryptography;

namespace Lockflight.Cli.Linking;

/// <summary>
/// The SHA-256 of the engine <c>lockflight link</c> copied into a project, as lower-case hex on one line in
/// <c>.lockflight/bin/lockflight.sha256</c>, followed by the engine's platform (<c>&lt;sha256&gt; &lt;rid&gt;</c>), which the shim
/// uses to find the engine lock's entry without detecting the platform itself (an older shim takes the first word only). It lies beside the engine so it has the same protection (the guard's
/// <c>.lockflight/**</c> rule, the sandbox's denial of <c>.lockflight/bin</c>). The hook shim compares the engine with it before
/// running it, and <c>doctor</c> before asking it for its version: an engine replaced after linking, or one without a record, is
/// not run.
/// </summary>
internal static class EngineRecord
{
    public const string FileName = "lockflight.sha256";

    public static string PathOf(string root) => Path.Combine(root, LinkCommand.RelativeBinDirectory, FileName);

    public static string HashOf(string file)
    {
        using FileStream stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Writes the record through a temporary file, so a hook never reads half of it.</summary>
    /// <param name="rid">The engine's platform; null on a platform no release ships, which records the hash alone.</param>
    public static void Write(string root, string hash, string? rid)
    {
        string path = PathOf(root);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, rid is null ? hash + "\n" : $"{hash} {rid}\n");
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The recorded hash; null when there is no record or it is empty.</summary>
    public static string? RecordedHash(string root)
    {
        string path = PathOf(root);
        string? first = File.Exists(path) ? File.ReadAllText(path).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : null;
        return string.IsNullOrEmpty(first) ? null : first;
    }

    public static EngineRecordStatus Check(string root, string binary)
    {
        string path = PathOf(root);
        if (!File.Exists(path))
        {
            return EngineRecordStatus.NotRecorded;
        }

        if (RecordedHash(root) is not string recorded)
        {
            return EngineRecordStatus.NotRecorded;
        }

        return string.Equals(recorded, HashOf(binary), StringComparison.OrdinalIgnoreCase) ? EngineRecordStatus.Matches : EngineRecordStatus.Differs;
    }
}
