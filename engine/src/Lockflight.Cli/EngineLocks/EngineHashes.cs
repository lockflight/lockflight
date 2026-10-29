namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// What a release says it ships (<c>lockflight.engine-hashes/v1</c>, in the pointer package and on the GitHub Release): the version,
/// the tagged commit, and per platform the executable's hash and its path inside the RID package.
/// </summary>
internal sealed record EngineHashes(string Version, string Commit, IReadOnlyDictionary<string, EngineBinary> Binaries)
{
    public const string Schema = "lockflight.engine-hashes/v1";

    /// <summary>The file's name at the pointer package's root and among the GitHub Release's assets.</summary>
    public const string FileName = "engine-hashes.json";
}
