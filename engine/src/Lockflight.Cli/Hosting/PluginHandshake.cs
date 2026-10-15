using System.Globalization;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// The plugin shim announces the hook protocol it speaks in <c>LOCKFLIGHT_PLUGIN_API</c>; an engine that speaks another one blocks
/// instead of misreading payloads. Direct invocations (tests, CI) set nothing and are not checked. At session start the shim also
/// passes the oldest engine the plugin works with (its <c>min-engine-version</c> file): the plugin updates from its marketplace on
/// its own while each project pins its engine, so an older pin is reported to the person.
/// </summary>
internal static class PluginHandshake
{
    public const int SupportedApi = 1;

    /// <summary>The file in the plugin's root naming the oldest engine version it works with (one line, a released version).</summary>
    public const string MinimumEngineFile = "min-engine-version";

    /// <summary>The API the plugin announced when it differs from the supported one; null when compatible or not announced.</summary>
    public static string? IncompatibleApi(EnvironmentReader environment)
    {
        string? announced = environment.Get(EnvironmentVariables.PluginApi);
        return string.IsNullOrEmpty(announced) || announced == SupportedApi.ToString(CultureInfo.InvariantCulture)
            ? null
            : announced;
    }

    /// <summary>
    /// The oldest engine the plugin works with when <paramref name="engineVersion"/> is older; null when it is not, or when the
    /// plugin announced none or something that is not a version.
    /// </summary>
    public static string? EngineTooOld(EnvironmentReader environment, string engineVersion) =>
        environment.Get(EnvironmentVariables.PluginMinimumEngine) is string announced
        && EngineVersion.Parse(announced) is EngineVersion minimum
        && EngineVersion.Parse(engineVersion) is EngineVersion engine
        && engine.CompareTo(minimum) < 0
            ? minimum.ToString()
            : null;
}
