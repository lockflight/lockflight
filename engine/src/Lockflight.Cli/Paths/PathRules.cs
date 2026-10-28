using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Matches file paths against manifest globs, independent of OS separators, drive forms, casing and Unicode normalization (both
/// sides are composed to NFC, see <see cref="UnicodeNormalization"/>).
/// </summary>
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
        string canonicalRoot = Canonicalize(root, baseDirectory: null);
        foreach (string form in WindowsParentSegments.WithCollapsed([filePath], workingDirectory))
        {
            if (ToRelative(canonicalRoot, Canonicalize(form, workingDirectory)) is string relative)
            {
                string composed = UnicodeNormalization.Compose(relative);
                if (globs.FirstOrDefault(glob => ToRegex(glob).IsMatch(composed)) is string match)
                {
                    return match;
                }
            }
        }

        return null;
    }

    /// <summary>Whether a '/'-separated path relative to the root matches a glob.</summary>
    public static bool Matches(string glob, string relativePath) => ToRegex(glob).IsMatch(UnicodeNormalization.Compose(relativePath));

    /// <summary>
    /// The path the OS would really touch: absolute, Windows short names expanded, NTFS streams and trailing dots dropped on Windows,
    /// symbolic links resolved, and each <c>..</c> taken from the directory a link before it really is (as <c>realpath</c> does).
    /// </summary>
    public static string Canonicalize(string path, string? baseDirectory)
    {
        // Expanded after joining: a relative path inherits the working directory's 8.3 segments (a runner's TEMP is often RUNNER~1).
        string absolute = WindowsPaths.ExpandShortNames(ToAbsoluteKeepingParents(path, baseDirectory));
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
        if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath[normalizedRoot.Length..];
        }

        // The root spelled in another Unicode normal form is the same directory; the path is then given composed.
        string composedRoot = UnicodeNormalization.Compose(normalizedRoot);
        string composedPath = UnicodeNormalization.Compose(normalizedPath);
        return composedPath.StartsWith(composedRoot, StringComparison.OrdinalIgnoreCase) ? composedPath[composedRoot.Length..] : null;
    }

    /// <summary>
    /// Separator-agnostic absolute path with '.' and '..' collapsed. Deliberately avoids Path.GetFullPath, which rejects
    /// Windows-style paths on Unix and vice versa.
    /// </summary>
    internal static string ToAbsolute(string path, string? baseDirectory) => Absolute(path, baseDirectory, collapseParents: true);

    /// <summary>
    /// As <see cref="ToAbsolute"/>, but every <c>..</c> stays in place: after a symbolic link it leads to the parent of the link's
    /// target, which only a walk of the file system can tell (<see cref="RealPath"/>).
    /// </summary>
    internal static string ToAbsoluteKeepingParents(string path, string? baseDirectory) => Absolute(path, baseDirectory, collapseParents: false);

    private static string Absolute(string path, string? baseDirectory, bool collapseParents)
    {
        string normalized = FromMsysDrive(Normalize(OperatingSystem.IsWindows() ? WindowsPaths.ToDosPath(path) ?? path : path));
        if (!IsRooted(normalized))
        {
            normalized = FromMsysDrive(Normalize(baseDirectory ?? Environment.CurrentDirectory)).TrimEnd(Separator) + Separator + normalized;
        }

        List<string> segments = [];
        foreach (string segment in normalized.Split(Separator))
        {
            if (segment == ParentSegment && !collapseParents)
            {
                segments.Add(segment);
            }
            else if (segment == ParentSegment)
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
        string pattern = Regex.Escape(UnicodeNormalization.Compose(Normalize(glob)))
            .Replace(@"\*\*/", "(.*/)?", StringComparison.Ordinal)
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", "[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", "[^/]", StringComparison.Ordinal);

        return new Regex($"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
