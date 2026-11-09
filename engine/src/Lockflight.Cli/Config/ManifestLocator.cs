namespace Lockflight.Cli.Config;

/// <summary>
/// Finds the nearest <c>lockflight.yaml</c> walking up from a directory and loads it. A unit repository of a workspace shell has its
/// own, which points at the shell (<c>shell:</c>); the vendored <c>lockflight.unit.yaml</c> of older releases is not read.
/// </summary>
internal static class ManifestLocator
{
    public static ManifestLoadResult Discover(string startDirectory) => Discover(startDirectory, readsVersion1: false);

    /// <summary>The manifest as <c>update --project</c> migrates it: a version 1 manifest is read, not refused.</summary>
    public static ManifestLoadResult DiscoverForMigration(string startDirectory) => Discover(startDirectory, readsVersion1: true);

    private static ManifestLoadResult Discover(string startDirectory, bool readsVersion1)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, Manifest.FileName);
            if (File.Exists(path))
            {
                return new ManifestParser(directory.FullName, path) { ReadsVersion1 = readsVersion1 }.Parse(File.ReadAllText(path));
            }
        }

        return ManifestLoadResult.NotFound;
    }
}
