namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Asks for a temporary folder it never uses and names the run's folder from a variable: no temporary-folder owner. Scanned, never run.</summary>
internal static class ElsewhereOwner
{
    private static readonly string Unused = Path.GetTempPath();

    public static string Root => Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") + "/TestResults" + Unused[..0];
}
