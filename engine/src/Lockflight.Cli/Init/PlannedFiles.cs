namespace Lockflight.Cli.Init;

/// <summary>
/// Files a command is about to write, '/'-separated and relative to the project root, counted as present by the lists that name
/// only what exists (<see cref="PermissionDenyRules"/>, <see cref="SandboxSettings"/>): <c>update</c> computes every file before
/// it writes any, and a new engine lock must be covered as soon as it lands.
/// </summary>
internal static class PlannedFiles
{
    /// <summary>Whether <paramref name="relativePath"/> is one of the files, or a directory that holds one.</summary>
    public static bool Covers(IReadOnlyCollection<string> planned, string relativePath) =>
        planned.Any(file => file == relativePath || file.StartsWith(relativePath + "/", StringComparison.Ordinal));
}
