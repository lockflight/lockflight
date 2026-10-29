namespace Lockflight.Cli.Routing;

/// <summary>
/// Where the policy that classified a change was read: the base ref as given, the commit it named, the manifest's blob id (null
/// when the base has no manifest) and the routing preset.
/// </summary>
internal sealed record PolicySource(string BaseRef, string Commit, string? ManifestBlob, string Preset);
