namespace Lockflight.Cli.Config;

/// <summary>
/// Finds the nearest <c>lockflight.yaml</c> walking up from a directory and loads it. Only when there is none — a unit repository
/// checked out on its own, as in its CI — the nearest <c>lockflight.unit.yaml</c> (the policy <c>workspace sync</c> vendors into
/// it) is the manifest; inside a workspace the shell's manifest wins — unless a unit holds a <c>lockflight.yaml</c> of its
/// own, which <c>doctor</c> reports as an error.
/// </summary>
internal static class ManifestLocator
{
    public static ManifestLoadResult Discover(string startDirectory) => Discover(startDirectory, readsVersion1: false);

    /// <summary>The manifest as <c>update --project</c> migrates it: a version 1 manifest is read, not refused.</summary>
    public static ManifestLoadResult DiscoverForMigration(string startDirectory) => Discover(startDirectory, readsVersion1: true);

    private static ManifestLoadResult Discover(string startDirectory, bool readsVersion1) =>
        // The vendored unit policy is written by workspace sync as version 1, and read as such while sync is still there.
        Find(startDirectory, Manifest.FileName, readsVersion1) ?? Find(startDirectory, Manifest.UnitFileName, readsVersion1: true) ?? ManifestLoadResult.NotFound;

    private static ManifestLoadResult? Find(string startDirectory, string fileName, bool readsVersion1)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, fileName);
            if (File.Exists(path))
            {
                return new ManifestParser(directory.FullName, path) { ReadsVersion1 = readsVersion1 }.Parse(File.ReadAllText(path));
            }
        }

        return null;
    }
}
