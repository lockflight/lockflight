namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Writes a file with no temporary folder in sight: a host-tampering finding. Scanned, never run.</summary>
internal static class PlainWriter
{
    public static void Write() => File.WriteAllText("results.xml", "<pass/>");
}
