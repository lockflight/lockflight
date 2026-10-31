namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Makes a temporary folder but also holds a path to the runner's results: no temporary-folder owner. Scanned, never run.</summary>
internal sealed class ResultsFixture
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-sample-");

    public static string Out => "/home/runner/work/x/TestResults";

    public string Folder => directory.FullName;
}
