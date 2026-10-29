namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest versions this engine reads. Version 1 (also a manifest without <c>version</c>) is read as version 2 with migration
/// defaults: the checks version 2 added only report, so a new engine never turns a green repository red by itself.
/// </summary>
internal static class ManifestVersions
{
    public const int Legacy = 1;
    public const int Current = 2;

    /// <summary>The top-level keys only a version 2 manifest may hold.</summary>
    public static IReadOnlyList<string> CurrentOnlyKeys { get; } = ["requiresEngine", "routing", "approvals", "autonomy", "shell"];
}
