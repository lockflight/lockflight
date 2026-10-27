using System.Security.Cryptography;

namespace Lockflight.Cli.Linking;

/// <summary>
/// The SHA-256 of the engine <c>lockflight link</c> copied into a project, as lower-case hex on one line in
/// <c>.lockflight/bin/lockflight.sha256</c>. It lies beside the engine so it has the same protection (the guard's
/// <c>.lockflight/**</c> rule, the sandbox's denial of <c>.lockflight/bin</c>). The hook shim compares the engine with it before
/// running it, and <c>doctor</c> before asking it for its version: an engine replaced after linking is not run.
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
    public static void Write(string root, string hash)
    {
        string path = PathOf(root);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, hash + "\n");
        File.Move(temporary, path, overwrite: true);
    }

    public static EngineRecordStatus Check(string root, string binary)
    {
        string path = PathOf(root);
        if (!File.Exists(path))
        {
            return EngineRecordStatus.NotRecorded;
        }

        string recorded = File.ReadAllText(path).Trim();
        return string.Equals(recorded, HashOf(binary), StringComparison.OrdinalIgnoreCase) ? EngineRecordStatus.Matches : EngineRecordStatus.Differs;
    }
}
