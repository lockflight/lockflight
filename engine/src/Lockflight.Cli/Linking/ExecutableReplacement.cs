namespace Lockflight.Cli.Linking;

/// <summary>
/// Puts a new engine binary in the place of an old one. A rename over the old binary is atomic, so a hook starting at that
/// moment finds one engine or the other, never none. Windows refuses that rename while the old binary runs (an update runs from
/// the very engine it replaces), but it allows renaming a running binary: the old one is moved aside first, which leaves the
/// path empty between the two renames, and removed by a later replacement once it no longer runs.
/// </summary>
internal static class ExecutableReplacement
{
    private const string AsideMarker = ".old-";

    /// <summary>Moves <paramref name="replacement"/> (a file in the same folder) to <paramref name="target"/>.</summary>
    public static void Replace(string replacement, string target)
    {
        try
        {
            File.Move(replacement, target, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && File.Exists(target))
        {
            File.Move(target, target + AsideMarker + Path.GetRandomFileName());
            File.Move(replacement, target);
        }

        RemoveSetAside(target);
    }

    private static void RemoveSetAside(string target)
    {
        string folder = Path.GetDirectoryName(target)!;
        foreach (string aside in Directory.EnumerateFiles(folder, Path.GetFileName(target) + AsideMarker + "*"))
        {
            try
            {
                File.Delete(aside);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Still running; the next replacement removes it.
            }
        }
    }
}
