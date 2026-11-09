using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Config;

/// <summary>
/// The manifest versions this engine knows. Only version 2 is read; version 1 (also a manifest without <c>version</c>) is refused
/// with the command that migrates it, and only the migration in <c>update --project</c> still reads it.
/// </summary>
internal static class ManifestVersions
{
    public const int Legacy = 1;
    public const int Current = 2;

    /// <summary>Why a version 1 manifest is refused, and the person's one-step fix.</summary>
    public static string Version1Refusal { get; } =
        $"version 1 manifests are no longer read; a person moves this one to version 2, keeping its settings, with `{EngineCommand.Name} update --project` in a separate terminal";

    /// <summary>The top-level keys only a version 2 manifest may hold.</summary>
    public static IReadOnlyList<string> CurrentOnlyKeys { get; } = ["requiresEngine", "routing", "approvals", "autonomy", "shell"];
}
