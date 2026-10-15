using System.Globalization;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// The plugin shim announces the hook protocol it speaks in <c>LOCKFLIGHT_PLUGIN_API</c>; an engine that speaks another one blocks
/// instead of misreading payloads. Direct invocations (tests, CI) set nothing and are not checked.
/// </summary>
internal static class PluginHandshake
{
    public const int SupportedApi = 1;

    /// <summary>The API the plugin announced when it differs from the supported one; null when compatible or not announced.</summary>
    public static string? IncompatibleApi(EnvironmentReader environment)
    {
        string? announced = environment.Get(EnvironmentVariables.PluginApi);
        return string.IsNullOrEmpty(announced) || announced == SupportedApi.ToString(CultureInfo.InvariantCulture)
            ? null
            : announced;
    }
}
