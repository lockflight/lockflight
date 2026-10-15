namespace Lockflight.Cli.Config;

/// <summary>What looking for and reading <c>lockflight.yaml</c> found.</summary>
internal enum ManifestLoadStatus
{
    /// <summary>No manifest from the start directory upwards — not a Lockflight project; hooks are a no-op.</summary>
    NotFound = 0,

    /// <summary>A manifest was found and is valid.</summary>
    Loaded = 1,

    /// <summary>A manifest was found but cannot be used (bad YAML, unknown key, wrong type); a human has to fix it.</summary>
    Invalid = 2,
}
