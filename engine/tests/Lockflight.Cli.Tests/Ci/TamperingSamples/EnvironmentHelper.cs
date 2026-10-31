namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Names a folder of the run from a variable. Scanned, never run.</summary>
internal static class EnvironmentHelper
{
    public static string Folder => Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") + "/out";
}
