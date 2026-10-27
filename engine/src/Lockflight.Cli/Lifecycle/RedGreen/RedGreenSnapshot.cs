using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// A feature's red/green record read once, for a decision a person gives in a session: the hash of its exact bytes (what a code
/// binds), the record parsed from those bytes, and why it could not be parsed. <see cref="MissingHash"/> stands for no record.
/// </summary>
internal sealed record RedGreenSnapshot(string Hash, RedGreenRecord? Record, string? Unreadable)
{
    /// <summary>The name the snapshot's hash is bound under.</summary>
    public const string BoundName = RedGreenRecord.FileName;

    public const string MissingHash = "none";

    public bool Exists => Hash != MissingHash;

    public static RedGreenSnapshot Of(Feature feature)
    {
        string path = RedGreenRecord.PathFor(feature);
        if (!File.Exists(path))
        {
            return new RedGreenSnapshot(MissingHash, null, null);
        }

        byte[] content = File.ReadAllBytes(path);
        string hash = Convert.ToHexStringLower(SHA256.HashData(content));
        try
        {
            return new RedGreenSnapshot(hash, RedGreenRecord.Parse(Encoding.UTF8.GetString(content)), null);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            return new RedGreenSnapshot(hash, null, exception.Message);
        }
    }
}
