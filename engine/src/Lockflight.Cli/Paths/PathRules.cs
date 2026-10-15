using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>Matches file paths against manifest globs, independent of OS separators, drive forms and casing.</summary>
internal static class PathRules
{
    private const char Separator = '/';
    private const string ParentSegment = "..";
    private const string CurrentSegment = ".";

    // A guard decision matches thousands of paths against the same few dozen globs; each glob is compiled once.
    private static readonly ConcurrentDictionary<string, Regex> Regexes = new(StringComparer.Ordinal);

    /// <summary>The first glob that <paramref name="filePath"/> matches relative to <paramref name="root"/>; null if none or outside the root.</summary>
    public static string? FindMatch(string root, string filePath, IEnumerable<string> globs) => FindMatch(root, filePath, root, globs);

    /// <summary>As <see cref="FindMatch(string, string, IEnumerable{string})"/>, resolving a relative path against <paramref name="workingDirectory"/>.</summary>
    public static string? FindMatch(string root, string filePath, string workingDirectory, IEnumerable<string> globs)
    {
        string? relative = ToRelative(Canonicalize(root, baseDirectory: null), Canonicalize(filePath, workingDirectory));
        if (relative is null)
        {
            return null;
        }

        return globs.FirstOrDefault(glob => ToRegex(glob).IsMatch(relative));
    }

    /// <summary>Whether a '/'-separated path relative to the root matches a glob.</summary>
    public static bool Matches(string glob, string relativePath) => ToRegex(glob).IsMatch(relativePath);

    /// <summary>
    /// The path the OS would really touch: absolute, Windows short names expanded, NTFS streams and trailing dots dropped on Windows,
    /// symbolic links resolved.
    /// </summary>
    public static string Canonicalize(string path, string? baseDirectory)
    {
        // Expanded after joining: a relative path inherits the working directory's 8.3 segments (a runner's TEMP is often RUNNER~1).
        string absolute = WindowsPaths.ExpandShortNames(ToAbsolute(path, baseDirectory));
        if (OperatingSystem.IsWindows())
        {
            absolute = WindowsPaths.ToWin32Target(absolute);
        }

        return RealPath.Resolve(absolute);
    }

    /// <summary>The path relative to the root with '/' separators; null when the path is outside the root.</summary>
    public static string? ToRelative(string root, string filePath)
    {
        string normalizedRoot = ToAbsolute(root, baseDirectory: null).TrimEnd(Separator) + Separator;
        string normalizedPath = ToAbsolute(filePath, normalizedRoot);

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? normalizedPath[normalizedRoot.Length..]
            : null;
    }

    /// <summary>
    /// Separator-agnostic absolute path with '.' and '..' collapsed. Deliberately avoids Path.GetFullPath, which rejects
    /// Windows-style paths on Unix and vice versa.
    /// </summary>
    internal static string ToAbsolute(string path, string? baseDirectory)
    {
        string normalized = FromMsysDrive(Normalize(path));
        if (!IsRooted(normalized))
        {
            normalized = FromMsysDrive(Normalize(baseDirectory ?? Environment.CurrentDirectory)).TrimEnd(Separator) + Separator + normalized;
        }

        List<string> segments = [];
        foreach (string segment in normalized.Split(Separator))
        {
            if (segment == ParentSegment)
            {
                if (segments.Count > 1)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != CurrentSegment && (segment.Length > 0 || segments.Count == 0))
            {
                segments.Add(segment);
            }
        }

        return string.Join(Separator, segments);
    }

    /// <summary>Git Bash / MSYS "/c/Users/x" is "C:/Users/x" on Windows.</summary>
    private static string FromMsysDrive(string path) =>
        OperatingSystem.IsWindows() && path.Length >= 3 && path[0] == Separator && char.IsAsciiLetter(path[1]) && path[2] == Separator
            ? $"{char.ToUpperInvariant(path[1])}:{path[2..]}"
            : path;

    private static bool IsRooted(string normalizedPath) =>
        normalizedPath.StartsWith(Separator)
        || (normalizedPath.Length >= 3 && char.IsAsciiLetter(normalizedPath[0]) && normalizedPath[1] == ':' && normalizedPath[2] == Separator);

    private static string Normalize(string path) => path.Replace('\\', Separator);

    private static Regex ToRegex(string glob) => Regexes.GetOrAdd(glob, CompileGlob);

    private static Regex CompileGlob(string glob)
    {
        string pattern = Regex.Escape(Normalize(glob))
            .Replace(@"\*\*/", "(.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal);

        return new Regex($"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
