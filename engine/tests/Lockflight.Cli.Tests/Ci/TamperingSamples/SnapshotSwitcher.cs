namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// Turns its own snapshot variable on before it writes: the guard is not off by default, so the write is a host-tampering
/// finding. Scanned, never run.
/// </summary>
internal static class SnapshotSwitcher
{
    private const string Variable = "FORCED_SNAPSHOTS";

    public static void TurnOn() => Environment.SetEnvironmentVariable(Variable, "record");

    public static void Write(string directory, string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }
    }
}
