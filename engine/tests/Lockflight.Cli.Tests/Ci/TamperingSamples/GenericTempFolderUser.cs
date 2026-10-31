namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>Writes into a generic fixture's temporary folder: no finding. Scanned, never run.</summary>
internal sealed class GenericTempFolderUser(GenericTempFolder<int> fixture)
{
    public void WriteSource() => File.WriteAllText(Path.Combine(fixture.Folder, "a.cs"), "class A;");
}
