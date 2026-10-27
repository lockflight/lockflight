using Lockflight.Cli.Doctor;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Adoption;

/// <summary>Where the Lockflight Spec Kit preset is on this machine, for the <c>adopt apply</c> line the plan prints.</summary>
internal static class PresetLocator
{
    private const string PresetDirectory = "speckit-preset";

    /// <summary>
    /// The preset of the installed plugin (Claude Code's record of installed plugins), else the one in the checkout an install from
    /// source was built from; null when neither is found.
    /// </summary>
    public static string? Find(string? userHome, string root)
    {
        if (userHome is null)
        {
            return null;
        }

        string?[] candidates =
        [
            PluginEngineCheck.InstallPath(root, userHome) is string plugin ? Path.Combine(plugin, PresetDirectory) : null,
            UserInstall.Source(userHome) is string checkout ? Path.Combine(checkout, "plugins", "lockflight", PresetDirectory) : null,
        ];
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
