using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests;

/// <summary>The 8.3 short name Windows gives an existing folder, asked of Windows itself; null when the volume keeps none.</summary>
internal static class WindowsShortNames
{
    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(60);

    public static async Task<string?> OfFolderAsync(string folder, CancellationToken cancellationToken)
    {
        string literal = folder.Replace("'", "''", StringComparison.Ordinal);
        ProcessResult shortPath = await ProcessRunner.RunAsync(
            "powershell", ["-NoProfile", "-NonInteractive", "-Command", $"(New-Object -ComObject Scripting.FileSystemObject).GetFolder('{literal}').ShortPath"],
            folder, TimeLimit, cancellationToken);
        string alias = shortPath.Output.Trim();
        return shortPath.ExitCode == 0 && alias.Contains('~', StringComparison.Ordinal) ? alias : null;
    }
}
