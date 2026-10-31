namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Writes that touch a temporary-folder owner but take their path from elsewhere: findings. Scanned, never run.</summary>
internal sealed class LaunderingUser(TempFolderOwner fixture, ResultsFixture results)
{
    public void WriteThroughTheFixturesResultsPath() => File.WriteAllText(Path.Combine(ResultsFixture.Out, results.Folder[..0] + "r.xml"), "x");

    public void WriteThroughAnEnvironmentHelper() => File.WriteAllText(Path.Combine(EnvironmentHelper.Folder, fixture.Folder[..0] + "r"), "x");

    public static void WriteThroughAnEnvironmentHelperAfterAskingForTemp() =>
        File.WriteAllText(Path.Combine(EnvironmentHelper.Folder, Path.GetTempPath()[..0] + "r"), "x");

    public void WriteAboveTheFixture() => File.WriteAllText(Path.Combine(fixture.Folder, "../r"), "x");

    public void WriteToARootedPath() => File.WriteAllText(fixture.Folder[..0] + "/tmp/r", "x");

    public static void WriteRootedAfterAskingForTemp() => File.WriteAllText(Path.GetTempPath()[..0] + "/home/runner/work/TestResults/x.trx", "<pass/>");

    public void WriteThroughAHelperOfAHelper() => File.WriteAllText(Path.Combine(IndirectPaths.Out, fixture.Folder[..0] + "r"), "x");

    public void WriteThroughAFieldAHelperFilled() => File.WriteAllText(Path.Combine(IndirectPaths.FromField, fixture.Folder[..0] + "r"), "x");

    public void WriteThroughAnAsyncHelper() => File.WriteAllText(Path.Combine(OutAsync().GetAwaiter().GetResult(), fixture.Folder[..0] + "r"), "x");

    [SampleData("/home/runner/work/TestResults/x.trx")]
    public void WriteAnAttributesPath(string path) => File.WriteAllText(Path.Combine(fixture.Folder, path), "<pass/>");

    private static async Task<string> OutAsync()
    {
        await Task.Yield();
        return Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? string.Empty;
    }
}
