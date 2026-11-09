using Lockflight.Cli.Config;

namespace Lockflight.Cli.Updating;

/// <summary>
/// A unit repository that still holds the policy the removed <c>workspace sync</c> vendored into it (<c>lockflight.unit.yaml</c>),
/// which <c>update --project</c> moves to the shell pointer (<c>init --unit-of</c>).
/// </summary>
internal static class VendoredUnit
{
    /// <summary>The repository at or above <paramref name="start"/> that holds the vendored file and no <c>lockflight.yaml</c> of its own; null when a <c>lockflight.yaml</c> comes first, or neither.</summary>
    public static string? Find(string start)
    {
        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Manifest.FileName)))
            {
                return null;
            }

            if (File.Exists(Path.Combine(directory.FullName, Manifest.UnitFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// The workspace shell the unit is checked out in, and the unit's path among its units: the nearest directory above the unit
    /// whose <c>lockflight.yaml</c> lists it. Null when none does.
    /// </summary>
    public static (string Shell, string UnitPath)? EnclosingShell(string unitRoot)
    {
        string unit = Path.GetFullPath(unitRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (DirectoryInfo? candidate = Directory.GetParent(unit); candidate is not null; candidate = candidate.Parent)
        {
            string manifestPath = Path.Combine(candidate.FullName, Manifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            Manifest? shell = new ManifestParser(candidate.FullName, manifestPath).Parse(File.ReadAllText(manifestPath)).Manifest;
            string relative = Path.GetRelativePath(candidate.FullName, unit).Replace('\\', '/');
            if (shell?.Units.FirstOrDefault(listed => listed.Path.Equals(relative, StringComparison.OrdinalIgnoreCase)) is Unit listedUnit)
            {
                return (candidate.FullName, listedUnit.Path);
            }
        }

        return null;
    }
}
