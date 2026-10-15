namespace Lockflight.Cli.Tests;

/// <summary>Deletes a test's temporary directory even when git left read-only object files in it (Windows refuses otherwise).</summary>
internal static class TempDirectories
{
    public static void DeleteRecursively(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            return;
        }

        foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes = FileAttributes.Normal;
        }

        directory.Delete(recursive: true);
    }
}
