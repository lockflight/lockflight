namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// Turns its snapshot variable on through a field and through a helper, so neither name is passed to
/// <c>SetEnvironmentVariable</c> as a literal: the writes behind those variables are host-tampering findings. Scanned, never run.
/// </summary>
internal static class SnapshotFieldSwitcher
{
    private static readonly string Name = "FIELD_SNAPSHOTS";

    public static void TurnOn()
    {
        Environment.SetEnvironmentVariable(Name, "record");
        Set("HELPER_SNAPSHOTS", "record");
    }

    public static void WriteBehindTheField(string directory, string text)
    {
        if (Environment.GetEnvironmentVariable("FIELD_SNAPSHOTS") == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", "case.verified.txt"), text);
        }
    }

    public static void WriteBehindTheHelper(string directory, string text)
    {
        if (Environment.GetEnvironmentVariable("HELPER_SNAPSHOTS") == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", "case.verified.txt"), text);
        }
    }

    private static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);
}
