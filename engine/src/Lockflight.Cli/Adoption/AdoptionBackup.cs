using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// What adoption may change, archived before apply under <c>.lockflight/backup/&lt;id&gt;.tar.gz</c> (git-ignored local state):
/// <c>CLAUDE.md</c>, <c>lockflight.yaml</c>, the adopted-features baseline, and the files of <c>.claude/</c> (except
/// <c>settings.local.json</c>, which adoption never touches and which may hold secrets, and <c>worktrees/</c>, which holds other
/// sessions' checkouts), <c>.specify/</c> and <c>specs/</c>. Symbolic links — files or folders — are never followed, so nothing
/// outside the project is archived or written. After a successful apply, <c>&lt;id&gt;.applied.json</c> records the hash of every
/// covered file: undo reverts only the files apply changed and nobody touched since, and reports the rest instead of overwriting
/// or deleting later work.
/// </summary>
internal static class AdoptionBackup
{
    public const string RelativeDirectory = ".lockflight/backup";

    private static readonly string[] Files = ["CLAUDE.md", "lockflight.yaml", ".lockflight/baselines/adopted-features.json"];

    private static readonly string[] Directories = [".claude", ".specify", "specs"];

    private static readonly string[] Excluded = [".claude/settings.local.json"];

    private static readonly string[] ExcludedDirectories = [".claude/worktrees"];

    /// <summary>A new backup id, unique in the backup folder: the time to the millisecond, then a counter if it is taken.</summary>
    public static string NewId(string root, DateTimeOffset now)
    {
        string id = now.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        string candidate = id;
        for (int suffix = 1; File.Exists(ArchiveOf(root, candidate)); suffix++)
        {
            candidate = $"{id}-{suffix}";
        }

        return candidate;
    }

    /// <summary>Archives the covered files and returns the archive's path.</summary>
    public static string Create(string root, string id)
    {
        string archive = ArchiveOf(root, id);
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
        using FileStream file = File.Create(archive);
        using GZipStream gzip = new(file, CompressionLevel.Optimal);
        using TarWriter tar = new(gzip, TarEntryFormat.Pax, leaveOpen: false);
        foreach (string relative in Covered(root))
        {
            tar.WriteEntry(Path.Combine(root, relative), relative);
        }

        return archive;
    }

    /// <summary>Records the covered files as apply left them, so undo can tell apply's changes from later work.</summary>
    public static void RecordApplied(string root, string id)
    {
        JsonObject hashes = [];
        foreach (string relative in Covered(root))
        {
            hashes[relative] = Hash(Path.Combine(root, relative));
        }

        JsonFiles.Write(AppliedOf(root, id), hashes);
    }

    /// <summary>The newest backup's id, or null.</summary>
    public static string? Latest(string root)
    {
        string directory = Path.Combine(root, RelativeDirectory);
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.tar.gz").Select(path => Path.GetFileName(path)[..^".tar.gz".Length]).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
    }

    public static bool Exists(string root, string id) => File.Exists(ArchiveOf(root, id));

    /// <summary>
    /// Makes the covered files exactly the backup again — files added since are removed. Only for rolling back an apply that
    /// failed part-way, when nothing but apply has touched them since the backup.
    /// </summary>
    public static void RestoreAll(string root, string id)
    {
        Dictionary<string, byte[]> archived = Read(root, id);
        foreach ((string relative, byte[] bytes) in archived)
        {
            Write(root, relative, bytes);
        }

        foreach (string added in Covered(root).Where(relative => !archived.ContainsKey(relative)).ToList())
        {
            File.Delete(Path.Combine(root, added));
        }
    }

    /// <summary>
    /// Undoes an apply: a file apply changed or created is restored or removed when it is still as apply left it; one apply deleted
    /// is restored when it is still absent. Anything changed since apply is left alone and reported (with <paramref name="force"/>,
    /// it is reverted too). Files apply did not touch are never touched.
    /// </summary>
    public static UndoResult Undo(string root, string id, bool force)
    {
        Dictionary<string, byte[]> before = Read(root, id);
        Dictionary<string, string> applied = AppliedHashes(root, id);
        int restored = 0;
        int removed = 0;
        List<string> kept = [];
        foreach ((string relative, string hashAfterApply) in applied)
        {
            string path = Path.Combine(root, relative);
            bool changedByApply = !before.TryGetValue(relative, out byte[]? original) || HashOf(original) != hashAfterApply;
            if (!changedByApply)
            {
                continue;
            }

            if (!force && (!File.Exists(path) || Hash(path) != hashAfterApply))
            {
                kept.Add(relative);
                continue;
            }

            if (original is null)
            {
                File.Delete(path);
                removed++;
            }
            else
            {
                Write(root, relative, original);
                restored++;
            }
        }

        foreach ((string relative, byte[] bytes) in before.Where(entry => !applied.ContainsKey(entry.Key)))
        {
            if (File.Exists(Path.Combine(root, relative)) && !force)
            {
                kept.Add(relative);
                continue;
            }

            Write(root, relative, bytes);
            restored++;
        }

        return new UndoResult(restored, removed, kept);
    }

    private static string ArchiveOf(string root, string id) => Path.Combine(root, RelativeDirectory, $"{id}.tar.gz");

    private static string AppliedOf(string root, string id) => Path.Combine(root, RelativeDirectory, $"{id}.applied.json");

    private static Dictionary<string, string> AppliedHashes(string root, string id)
    {
        string path = AppliedOf(root, id);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"backup {id} has no record of what apply changed ({Path.GetFileName(path)}); restore by hand from {Path.GetFileName(ArchiveOf(root, id))} or git ref refs/lockflight/backup/{id}");
        }

        return JsonNode.Parse(File.ReadAllText(path))!.AsObject().ToDictionary(entry => entry.Key, entry => entry.Value!.GetValue<string>(), StringComparer.Ordinal);
    }

    private static Dictionary<string, byte[]> Read(string root, string id)
    {
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        using FileStream file = File.OpenRead(ArchiveOf(root, id));
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using TarReader tar = new(gzip);
        while (tar.GetNextEntry() is TarEntry entry)
        {
            string relative = entry.Name.Replace('\\', '/');
            if (relative.Split('/').Contains("..") || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException($"the backup holds an unsafe path: {relative}");
            }

            using MemoryStream bytes = new();
            entry.DataStream?.CopyTo(bytes);
            files[relative] = bytes.ToArray();
        }

        return files;
    }

    /// <summary>Writes a file under the root, refusing to write through a symbolic link anywhere on its path.</summary>
    private static void Write(string root, string relative, byte[] bytes)
    {
        string current = root;
        string[] segments = relative.Split('/');
        foreach (string segment in segments.SkipLast(1))
        {
            current = Path.Combine(current, segment);
            if (new DirectoryInfo(current) is { Exists: true, LinkTarget: not null })
            {
                throw new InvalidOperationException($"{relative} lies behind a symbolic link; restore it by hand");
            }
        }

        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (new FileInfo(path) is { Exists: true, LinkTarget: not null })
        {
            throw new InvalidOperationException($"{relative} is a symbolic link; restore it by hand");
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Every covered regular file that exists now, relative to the root and '/'-separated; links are not followed.</summary>
    private static IEnumerable<string> Covered(string root)
    {
        foreach (string file in Files.Where(file => new FileInfo(Path.Combine(root, file)) is { Exists: true, LinkTarget: null }))
        {
            yield return file;
        }

        foreach (string directory in Directories)
        {
            foreach (string relative in Walk(root, directory))
            {
                yield return relative;
            }
        }
    }

    private static IEnumerable<string> Walk(string root, string relativeDirectory)
    {
        DirectoryInfo directory = new(Path.Combine(root, relativeDirectory));
        if (!directory.Exists || directory.LinkTarget is not null || ExcludedDirectories.Contains(relativeDirectory, StringComparer.Ordinal))
        {
            yield break;
        }

        foreach (FileInfo file in directory.EnumerateFiles().OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            string relative = $"{relativeDirectory}/{file.Name}";
            if (file.LinkTarget is null && !Excluded.Contains(relative, StringComparer.Ordinal))
            {
                yield return relative;
            }
        }

        foreach (DirectoryInfo child in directory.EnumerateDirectories().OrderBy(child => child.Name, StringComparer.Ordinal))
        {
            foreach (string relative in Walk(root, $"{relativeDirectory}/{child.Name}"))
            {
                yield return relative;
            }
        }
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string HashOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
