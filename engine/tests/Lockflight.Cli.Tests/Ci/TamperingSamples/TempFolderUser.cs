namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>A test class that writes into a fixture's temporary folder, directly and from an async method: no finding. Scanned, never run.</summary>
internal sealed class TempFolderUser(TempFolderOwner fixture)
{
    public void WriteSource() => File.WriteAllText(Path.Combine(fixture.Folder, "a.cs"), "class A;");

    public async Task WriteSourceAsync(CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, "b.cs"), "class B;", cancellationToken);
}
