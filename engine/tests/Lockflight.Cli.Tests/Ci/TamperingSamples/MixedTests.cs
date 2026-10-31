namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>
/// A test class where one method reads a variable and the others write into the fixture's temporary folder: the writes are no
/// finding, only what reads the variable is marked. Scanned, never run.
/// </summary>
internal sealed class MixedTests(TempFolderOwner fixture)
{
    public static bool OnCi => Environment.GetEnvironmentVariable("CI") is not null;

    private readonly bool onCiAtStart = Environment.GetEnvironmentVariable("CI") is not null;

    public bool OnCiAtStart => onCiAtStart;

    public void WriteSource() => File.WriteAllText(Path.Combine(fixture.Folder, "a.cs"), "class A;");

    public async Task WriteSourceAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, "b.cs"), "class B;", cancellationToken);
    }
}
