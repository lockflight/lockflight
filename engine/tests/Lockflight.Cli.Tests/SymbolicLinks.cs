namespace Lockflight.Cli.Tests;

/// <summary>
/// Whether this process can create symbolic links. Everywhere but Windows it can; Windows allows it to an elevated process or in
/// developer mode, which the hosted CI runners have, so the link tests run there and skip only where Windows refuses.
/// </summary>
internal static class SymbolicLinks
{
    public const string SkipReason = "Windows refuses this process symbolic links (it needs developer mode or an elevated session).";

    private static readonly Lazy<bool> Probe = new(CanCreateLink);

    public static bool CanCreate => Probe.Value;

    private static bool CanCreateLink()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        DirectoryInfo scratch = Directory.CreateTempSubdirectory("lockflight-link-probe-");
        string fileLink = Path.Combine(scratch.FullName, "file-link");
        string directoryLink = Path.Combine(scratch.FullName, "directory-link");
        try
        {
            File.WriteAllText(Path.Combine(scratch.FullName, "target"), string.Empty);
            Directory.CreateDirectory(Path.Combine(scratch.FullName, "target-directory"));
            File.CreateSymbolicLink(fileLink, Path.Combine(scratch.FullName, "target"));
            Directory.CreateSymbolicLink(directoryLink, Path.Combine(scratch.FullName, "target-directory"));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            File.Delete(fileLink);
            if (Directory.Exists(directoryLink))
            {
                Directory.Delete(directoryLink);
            }

            TempDirectories.DeleteRecursively(scratch);
        }
    }
}
