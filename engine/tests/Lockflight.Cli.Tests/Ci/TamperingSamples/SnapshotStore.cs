namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// A snapshot store that writes its case files only in record mode, behind a snapshot variable that is off by default: no
/// host-tampering finding. Scanned, never run.
/// </summary>
internal sealed class SnapshotStore(string directory)
{
    private const string Variable = "SHOP_SNAPSHOTS";

    public void Save(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            Directory.CreateDirectory(Path.Combine(directory, "Snapshots"));
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }
    }

    public async Task SaveRenderedAsync(string name, Func<Task<string>> render)
    {
        string text = await render();
        if (string.Equals(Environment.GetEnvironmentVariable(Variable), "record", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.Combine(directory, "__snapshots__"));
            File.WriteAllText(Path.Combine(directory, "__snapshots__", name + ".json"), text);
        }
    }
}
