using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Paths;

/// <summary>
/// Windows path forms that alias ordinary ones: 8.3 short names ("RUNNER~1" for "runneradmin"), expanded through the file system,
/// and the Win32 namespaces (<c>\\?\C:\…</c>, <c>\\.\C:\…</c>, <c>\\?\UNC\…</c>) and administrative shares of this machine
/// (<c>\\localhost\C$\…</c>), mapped to their drive. Comparing strings would let either slip past protected-path rules.
/// </summary>
internal static partial class WindowsPaths
{
    private const int MaxPathBuffer = 32768;
    private const char WindowsSeparator = '\\';
    private const string UncPrefix = "UNC";

    /// <summary>Host names that are this machine in a UNC path (besides its own name).</summary>
    private static readonly HashSet<string> LocalHosts = new(["localhost", "127.0.0.1", "::1", "[::1]", "."], StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// The drive path a namespace or administrative-share path names (<c>\\?\C:\x</c>, <c>\\.\C:\x</c>,
    /// <c>\\?\UNC\localhost\C$\x</c> and <c>\\localhost\C$\x</c> are all <c>C:\x</c>), with backslashes; a share on another machine
    /// (<c>\\server\share\x</c>) and any other path are returned as they are. Null when the path names something the guard cannot
    /// map to a drive: a volume GUID, a device, another namespace object, a non-administrative share of this machine, or an
    /// administrative share on a host it cannot tell apart from this one. Pure string logic, usable on every OS.
    /// </summary>
    public static string? ToDosPath(string path)
    {
        if (path.Length < 2 || !IsSeparator(path[0]) || !IsSeparator(path[1]))
        {
            return path;
        }

        string[] parts = path.Replace('/', WindowsSeparator)[2..].Split(WindowsSeparator);
        if (parts[0] is "?" or ".")
        {
            return Namespaced(parts[1..]);
        }

        return Share(parts);
    }

    /// <summary>What follows <c>\\?\</c> or <c>\\.\</c>: a drive (<c>C:\…</c>), <c>UNC\host\share\…</c>, or something unmappable.</summary>
    private static string? Namespaced(string[] parts)
    {
        if (parts.Length > 0 && parts[0].Length == 2 && char.IsAsciiLetter(parts[0][0]) && parts[0][1] == ':')
        {
            return string.Join(WindowsSeparator, parts);
        }

        return parts.Length > 0 && parts[0].Equals(UncPrefix, StringComparison.OrdinalIgnoreCase) ? Share(parts[1..]) : null;
    }

    /// <summary><c>host\share\rest</c>: a drive's administrative share on this machine is the drive.</summary>
    private static string? Share(string[] parts)
    {
        if (parts.Length < 2)
        {
            return null;
        }

        bool local = LocalHosts.Contains(parts[0]) || parts[0].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        bool administrative = parts[1].Length == 2 && char.IsAsciiLetter(parts[1][0]) && parts[1][1] == '$';
        if (administrative)
        {
            return local ? string.Join(WindowsSeparator, [$"{parts[1][0]}:", .. parts[2..]]) : null;
        }

        return local ? null : $"{WindowsSeparator}{WindowsSeparator}{string.Join(WindowsSeparator, parts)}";
    }

    private static bool IsSeparator(char character) => character is '/' or WindowsSeparator;

    /// <summary>True when a segment still looks like an unexpanded 8.3 alias (e.g. "PROGRA~1").</summary>
    public static bool HasShortNameSegment(string path) => ShortNameSegment().IsMatch(path);

    [GeneratedRegex(@"(^|[\\/])[^\\/~]{1,6}~\d+([.][^\\/]{0,3})?($|[\\/])")]
    private static partial Regex ShortNameSegment();

    [LibraryImport("kernel32.dll", EntryPoint = "GetLongPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint bufferLength);
}
