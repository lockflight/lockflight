using Lockflight.Cli.Config;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The policy a pull request is judged by, read from the target branch's tip (<see cref="Commit"/>), never from the pull request:
/// the manifest (null when the base has none, so the defaults apply), its SHA-256 and blob id, and the policy files present there.
/// </summary>
internal sealed record BasePolicy(string Commit, Manifest? Manifest, string? ManifestSha256, string? ManifestBlob, IReadOnlyList<string> Files)
{
    public CiSettings Ci => Manifest?.Ci ?? CiSettings.Default;
}
