namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>A generic fixture that makes its own temporary folder. Scanned, never run.</summary>
/// <typeparam name="T">Any type: only the instantiation matters to the scan.</typeparam>
internal sealed class GenericTempFolder<T>
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-sample-");

    public string Folder => directory.FullName;
}
