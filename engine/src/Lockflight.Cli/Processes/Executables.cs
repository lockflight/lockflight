namespace Lockflight.Cli.Processes;

/// <summary>Finds optional external tools before starting them, so a missing tool is a verdict rather than a crash.</summary>
internal static class Executables
{
    /// <summary>The explicit path when it exists, otherwise the first match on PATH (with <c>.exe</c> on Windows); null when absent.</summary>
    public static string? Find(string name, string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath))
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        string fileName = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}
