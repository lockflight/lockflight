using Lockflight.Cli.Config;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// Loads the manifest a command cannot work without. A missing manifest and an invalid one are different problems with different
/// fixes, so each gets its own message on stderr; the caller then exits with <see cref="ExitCode.Config"/>.
/// </summary>
internal static class ManifestRequirement
{
    /// <summary>The manifest found from <paramref name="startDirectory"/> upwards; null when it is missing or invalid (already reported).</summary>
    public static async Task<Manifest?> LoadAsync(CliContext context, string startDirectory, CancellationToken cancellationToken)
    {
        ManifestLoadResult loadResult = ManifestLocator.Discover(startDirectory);
        if (loadResult.Manifest is Manifest manifest)
        {
            return manifest;
        }

        string message = loadResult.Error is ManifestError error ? Invalid(error) : Missing(startDirectory);
        await context.Error.WriteLineAsync(message.AsMemory(), cancellationToken);
        return null;
    }

    public static string Invalid(ManifestError error) =>
        $"lockflight: {error.Describe()}. Fix {Manifest.FileName} (a person's change: it is protected) and run the command again.";

    private static string Missing(string startDirectory) =>
        $"lockflight: no {Manifest.FileName} from {startDirectory} upwards; run '{EngineCommand.Name} init' first";
}
