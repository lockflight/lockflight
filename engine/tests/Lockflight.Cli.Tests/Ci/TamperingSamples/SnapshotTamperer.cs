namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// Snapshot-looking writes that are still host-tampering findings: not behind an off-by-default snapshot variable, or naming a
/// file the test host or build reads, or more than writing a case file. Scanned, never run.
/// </summary>
internal sealed class SnapshotTamperer(string directory)
{
    private const string Variable = "SHOP_SNAPSHOTS";

    private static readonly string RunnerConfig = "xunit.runner.json";

    public void WriteWithoutAGuard(string name, string text) =>
        File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);

    public void WriteAfterTheGuard(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            Directory.CreateDirectory(Path.Combine(directory, "Snapshots"));
        }

        File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
    }

    public void WriteUnlessSwitchedOff(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) != "off")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }
    }

    public void WriteOnAVariableTheRunnerSets(string name, string text)
    {
        if (Environment.GetEnvironmentVariable("CONTINUOUS_INTEGRATION") == "true")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }
    }

    public void WriteOnTheGuardOrAFlag(string name, string text, bool force)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record" || force)
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }
    }

    public void WriteTheProjectFile(string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Shop.Tests.csproj"), text);
        }
    }

    public void WriteASource(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.cs"), text);
        }
    }

    public void WriteTheRunnerConfig(string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "xunit.runner.json"), text);
        }
    }

    public void WriteTheBuildProps(string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), text);
        }
    }

    public void WriteAboveTheProject(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, "..", name + ".verified.txt"), text);
        }
    }

    public void DeleteInRecordMode(string name)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.Delete(Path.Combine(directory, "Snapshots", name + ".verified.txt"));
        }
    }

    public void WriteOnAnotherLocal(string name, string text, bool force)
    {
        bool go = force;
        bool record = Environment.GetEnvironmentVariable(Variable) == "record";
        if (go)
        {
            File.WriteAllText(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text);
        }

        GC.KeepAlive(record);
    }

    public void WriteAFileAFieldNames(string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(directory, RunnerConfig), text);
        }
    }

    /// <summary>Awaits inside the guard: the state machine resumes inside the block, so the scan cannot tell it is guarded.</summary>
    public async Task WriteAwaitedInRecordMode(string name, string text, CancellationToken cancellationToken)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Snapshots", name + ".verified.txt"), text, cancellationToken);
        }
    }

    public static void WriteBesideTheBuildOutput(string name, string text)
    {
        if (Environment.GetEnvironmentVariable(Variable) == "record")
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "Snapshots", name + ".verified.txt"), text);
        }
    }
}
