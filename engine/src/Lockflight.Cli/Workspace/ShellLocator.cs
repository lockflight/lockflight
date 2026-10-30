using Lockflight.Cli.Config;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// Finds the workspace shell a unit repository sits in on a developer's machine: the nearest directory above the unit whose
/// <c>lockflight.yaml</c> lists the unit at the path the pointer names. CI has no such directory and clones the shell instead.
/// </summary>
internal static class ShellLocator
{
    /// <summary>The enclosing shell's directory; null when the unit is not checked out inside its shell.</summary>
    public static string? FindEnclosing(string unitRoot, ShellSettings pointer)
    {
        string unit = Path.GetFullPath(unitRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (DirectoryInfo? candidate = Directory.GetParent(unit); candidate is not null; candidate = candidate.Parent)
        {
            string manifestPath = Path.Combine(candidate.FullName, Manifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            string expected = Path.Combine(candidate.FullName, pointer.Unit.Replace('/', Path.DirectorySeparatorChar));
            Manifest? manifest = new ManifestParser(candidate.FullName, manifestPath).Parse(File.ReadAllText(manifestPath)).Manifest;
            bool listsThisUnit = manifest?.Units.Any(listed => listed.Path.Equals(pointer.Unit, StringComparison.OrdinalIgnoreCase)) == true;
            if (listsThisUnit && string.Equals(Path.GetFullPath(expected), unit, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.FullName;
            }
        }

        return null;
    }
}
