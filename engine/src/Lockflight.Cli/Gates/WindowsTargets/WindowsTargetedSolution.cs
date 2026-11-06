namespace Lockflight.Cli.Gates.WindowsTargets;

/// <summary>
/// A solution with projects that target Windows (a <c>net*-windows</c> framework): WPF, Windows Forms, WinUI.
/// </summary>
/// <param name="Solution">The solution file's full path.</param>
/// <param name="Entries">Every project the solution lists, as it writes their paths.</param>
/// <param name="WindowsEntries">The entries whose project targets Windows, in the solution's order.</param>
/// <param name="TestEntries">The entries that are test projects (by their project file or a <c>Directory.Build.props</c> above it).</param>
internal sealed record WindowsTargetedSolution(string Solution, IReadOnlyList<string> Entries, IReadOnlyList<string> WindowsEntries, IReadOnlySet<string> TestEntries)
{
    private const int MaxListed = 4;

    /// <summary>The Windows-targeted test projects of the given entries, by file name.</summary>
    public IReadOnlyList<string> WindowsTestsAmong(IEnumerable<string> entries) =>
        [.. entries.Where(entry => WindowsEntries.Contains(entry, StringComparer.Ordinal) && TestEntries.Contains(entry)).Select(ProjectName)];

    /// <summary>The note a gate step adds when it leaves these test projects out.</summary>
    public static string LeftOutNote(IReadOnlyList<string> projects)
    {
        string listed = string.Join(", ", projects.Take(MaxListed));
        string more = projects.Count > MaxListed ? $" and {projects.Count - MaxListed} more" : string.Empty;
        return $"left out off Windows: {listed}{more} (they target Windows and cannot start here; they run on Windows and on a Windows CI runner)";
    }

    private static string ProjectName(string entry) => Path.GetFileNameWithoutExtension(entry.Replace('\\', '/').Split('/')[^1]);
}
