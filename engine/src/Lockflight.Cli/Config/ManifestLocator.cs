namespace Lockflight.Cli.Config;

/// <summary>
/// Finds the nearest <c>lockflight.yaml</c> walking up from a directory and loads it. Only when there is none — a unit repository
/// checked out on its own, as in its CI — the nearest <c>lockflight.unit.yaml</c> (the policy <c>workspace sync</c> vendors into
/// it) is the manifest; inside a workspace the shell's manifest wins — unless a unit holds a <c>lockflight.yaml</c> of its
/// own, which <c>doctor</c> reports as an error.
/// </summary>
internal static class ManifestLocator
{
    public static ManifestLoadResult Discover(string startDirectory) =>
        Find(startDirectory, Manifest.FileName) ?? Find(startDirectory, Manifest.UnitFileName) ?? ManifestLoadResult.NotFound;

    private static ManifestLoadResult? Find(string startDirectory, string fileName)
    {
        for (DirectoryInfo? directory = new(startDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, fileName);
            if (File.Exists(path))
            {
                return new ManifestParser(directory.FullName, path).Parse(File.ReadAllText(path));
            }
        }

        return null;
    }
}
