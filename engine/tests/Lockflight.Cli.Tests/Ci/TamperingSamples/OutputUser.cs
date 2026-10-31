namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// A type that asks for a temporary folder in one method and writes a relative path from a field that is no temporary folder in
/// another: the write is a finding. Scanned, never run.
/// </summary>
internal sealed class OutputUser
{
    private readonly string name = "coverage";

    public static string Temp() => Path.GetTempPath();

    public void WriteRelative() => File.WriteAllText(name + ".xml", "x");
}
