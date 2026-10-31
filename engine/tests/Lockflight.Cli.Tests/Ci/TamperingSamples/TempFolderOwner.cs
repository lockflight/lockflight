namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>A fixture that makes its own temporary folder and writes into it: no host-tampering finding. Scanned, never run.</summary>
internal sealed class TempFolderOwner
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-sample-");

    public string Folder => directory.FullName;

    public void WriteNote() => File.WriteAllText(Path.Combine(directory.FullName, "note.txt"), "x");

    /// <summary>Writes where the runner's results could be, without touching its temporary folder: still a finding.</summary>
    public static void WriteResults() => File.WriteAllText("results.xml", "<pass/>");

    /// <summary>Touches the temporary folder but names a folder of the run from an environment variable: still a finding.</summary>
    public void WriteToTheRun() => File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? directory.FullName, "x"), "x");
}
