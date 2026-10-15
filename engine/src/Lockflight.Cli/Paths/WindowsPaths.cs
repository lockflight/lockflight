using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Windows 8.3 short names ("RUNNER~1") alias long ones ("runneradmin"); comparing strings would let a short-name path slip
/// past protected-path rules, so they are expanded through the file system.
/// </summary>
internal static partial class WindowsPaths
{
    private const int MaxPathBuffer = 32768;
    private const char WindowsSeparator = '\\';

    /// <summary>Expands 8.3 segments of the longest existing prefix; returns the input unchanged off Windows.</summary>
    public static string ExpandShortNames(string path)
    {
        if (!OperatingSystem.IsWindows() || !path.Contains('~', StringComparison.Ordinal))
        {
            return path;
        }

        string existing = path.Replace('/', WindowsSeparator);
        string remainder = string.Empty;
        while (existing.Length > 0 && !Path.Exists(existing))
        {
            int cut = existing.LastIndexOf(WindowsSeparator);
            if (cut <= 0)
            {
                return path;
            }

            remainder = existing[cut..] + remainder;
            existing = existing[..cut];
        }

        char[] buffer = new char[MaxPathBuffer];
        uint length = GetLongPathNameW(existing, buffer, (uint)buffer.Length);
        return length == 0 ? path : new string(buffer, 0, (int)length) + remainder;
    }

    /// <summary>
    /// What Windows would actually open: NTFS alternate-data-stream suffixes (<c>key.txt:hidden</c>) are dropped and trailing dots
    /// and spaces on each segment are trimmed (<c>secrets.</c> opens <c>secrets</c>). Pure string logic, usable on every OS.
    /// </summary>
    public static string ToWin32Target(string path)
    {
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            bool isDrive = index == 0 && segment.Length == 2 && segment[1] == ':';
            int streamSeparator = isDrive ? -1 : segment.IndexOf(':', StringComparison.Ordinal);
            if (streamSeparator >= 0)
            {
                segment = segment[..streamSeparator];
            }

            segments[index] = segment is "." or ".." ? segment : segment.TrimEnd('.', ' ');
        }

        return string.Join('/', segments);
    }

    /// <summary>True when a segment still looks like an unexpanded 8.3 alias (e.g. "PROGRA~1").</summary>
    public static bool HasShortNameSegment(string path) => ShortNameSegment().IsMatch(path);

    [GeneratedRegex(@"(^|[\\/])[^\\/~]{1,6}~\d+([.][^\\/]{0,3})?($|[\\/])")]
    private static partial Regex ShortNameSegment();

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint bufferLength);
}
