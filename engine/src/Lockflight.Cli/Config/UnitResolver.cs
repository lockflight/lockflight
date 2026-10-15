namespace Lockflight.Cli.Config;

/// <summary>Which unit of a workspace owns a path: the unit whose directory contains it (units never nest).</summary>
internal static class UnitResolver
{
    /// <summary>The unit containing <paramref name="absolutePath"/>, or null for a path outside every unit (the shell itself).</summary>
    public static Unit? Owning(Manifest manifest, string absolutePath)
    {
        string relative = Path.GetRelativePath(manifest.Root, Path.GetFullPath(absolutePath)).Replace('\\', '/');
        return manifest.Units.FirstOrDefault(unit =>
            relative.Equals(unit.Path, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(unit.Path + "/", StringComparison.OrdinalIgnoreCase));
    }
}
