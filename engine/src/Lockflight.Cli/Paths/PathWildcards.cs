namespace Lockflight.Cli.Paths;

/// <summary>Shell wildcards (<c>*</c>, <c>?</c>, <c>[…]</c>) in a path.</summary>
internal static class PathWildcards
{
    public static bool HasWildcard(string path) => GlobOverlap.HasWildcard(path);
}
