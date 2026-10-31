namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Writes through types that look like fixtures but are no proof of a temporary folder: findings. Scanned, never run.</summary>
internal sealed class ElsewhereUser(TempFolderOwner fixture)
{
    public static void WriteIntoTheRun() => File.WriteAllText(Path.Combine(ElsewhereOwner.Root, "x.trx"), "<pass/>");

    public void WriteToTheRun() => File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP") ?? fixture.Folder, "x"), "x");

    public void WriteResultsBesideTheFixture() => File.WriteAllText(fixture.Folder.Length > 0 ? "TestResults/r.trx" : fixture.Folder, "<pass/>");
}
