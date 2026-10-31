namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Passes on a folder of the run that a helper read from a variable. Scanned, never run.</summary>
internal static class IndirectPaths
{
    private static readonly string Cached = EnvironmentHelper.Folder;

    public static string Out => EnvironmentHelper.Folder;

    public static string FromField => Cached;
}
