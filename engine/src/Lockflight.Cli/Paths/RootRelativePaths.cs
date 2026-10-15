namespace Lockflight.Cli.Paths;

/// <summary>
/// Paths relative to a project root after canonicalization (<see cref="PathRules.Canonicalize"/>), memoized for one guard
/// decision: a wildcard can expand to thousands of siblings, and each would otherwise walk every symbolic link from the file
/// system root again. On Unix a directory's real path is reused for its children; a child that is itself a link is resolved in
/// full.
/// </summary>
internal sealed class RootRelativePaths(string root)
{
    private readonly Dictionary<string, string> canonicalByAbsolute = new(StringComparer.Ordinal);
    private string? canonicalRoot;

    /// <summary>The '/'-separated path relative to the root ("" for the root itself); null when it lies outside the root.</summary>
    public string? Relative(string path, string workingDirectory)
    {
        canonicalRoot ??= PathRules.ToAbsolute(PathRules.Canonicalize(root, baseDirectory: null), baseDirectory: null).TrimEnd('/');
        // The per-segment walk needs the Windows forms resolved first, as PathRules.Canonicalize does.
        string canonical = PathRules.ToAbsolute(Canonical(CanonicalInput(path, workingDirectory)), baseDirectory: null).TrimEnd('/');
        return string.Equals(canonicalRoot, canonical, StringComparison.OrdinalIgnoreCase) ? string.Empty : PathRules.ToRelative(canonicalRoot, canonical);
    }

    /// <summary>Whether the path is the root or a directory above it (<c>..</c>, <c>~</c> for a project under the home folder).</summary>
    public bool ContainsRoot(string path, string workingDirectory)
    {
        if (Relative(path, workingDirectory) is { Length: 0 })
        {
            return true;
        }

        // The file system root ("/") normalizes to "" and a drive root to "C:"; both are taken as they are, not resolved further.
        string absolute = CanonicalInput(path, workingDirectory).TrimEnd('/');
        bool isRoot = absolute.Length == 0 || (absolute.Length == 2 && absolute[1] == ':');
        string canonical = isRoot ? absolute : PathRules.ToAbsolute(Canonical(absolute), baseDirectory: null).TrimEnd('/');
        return canonicalRoot!.StartsWith(canonical + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalInput(string path, string workingDirectory)
    {
        string absolute = PathRules.ToAbsolute(path, workingDirectory);
        return OperatingSystem.IsWindows() ? WindowsPaths.ToWin32Target(WindowsPaths.ExpandShortNames(absolute)) : absolute;
    }

    private string Canonical(string absolute)
    {
        if (canonicalByAbsolute.TryGetValue(absolute, out string? cached))
        {
            return cached;
        }

        int lastSeparator = absolute.LastIndexOf('/');
        string parent = lastSeparator > 0 ? absolute[..lastSeparator] : string.Empty;
        string canonical;
        if (parent.Length == 0 || (parent.Length == 2 && parent[1] == ':'))
        {
            canonical = PathRules.Canonicalize(absolute, baseDirectory: null);
        }
        else
        {
            string candidate = $"{Canonical(parent).TrimEnd('/')}/{absolute[(lastSeparator + 1)..]}";
            canonical = IsLink(candidate) ? PathRules.Canonicalize(candidate, baseDirectory: null) : candidate;
        }

        canonicalByAbsolute[absolute] = canonical;
        return canonical;
    }

    /// <summary>One attribute read instead of an existence check plus a link read; a missing entry is no link.</summary>
    private static bool IsLink(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
