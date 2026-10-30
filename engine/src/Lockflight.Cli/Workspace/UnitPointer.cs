using Lockflight.Cli.Config;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// Whether a unit of a workspace reads the shell's policy through a pointer (its own <c>lockflight.yaml</c> with <c>shell:</c>
/// naming this unit) instead of a vendored copy: such a unit is not synced, and its own manifest is expected.
/// </summary>
internal static class UnitPointer
{
    public static bool PointsToShell(string unitDirectory, Unit unit)
    {
        string path = Path.Combine(unitDirectory, Manifest.FileName);
        return File.Exists(path)
            && new ManifestParser(unitDirectory, path).Parse(File.ReadAllText(path)).Manifest?.Shell is ShellSettings shell
            && shell.Unit.Equals(unit.Path, StringComparison.OrdinalIgnoreCase);
    }
}
