using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The content hashes (SHA-256, hex) of every file in the locked test projects, taken when a red starts and again for the amended
/// files when an amend's red is accepted. <c>green</c> and the review of record refuse while a file differs from them: a locked test
/// file changed, appeared or disappeared behind the guard (another editor, a script, a write the guard did not see). Left out, because
/// builds, test runs and editors write them: <c>bin/</c>, <c>obj/</c> and <c>node_modules/</c>; files in <c>TestResults/</c> and
/// snapshot-test <c>.received.</c> files that are not C# sources; dot-files and dot-folders; editor and IDE files (<c>*.user</c>,
/// <c>*.swp</c>, <c>Thumbs.db</c>). Line endings do not count: CRLF reads as LF. A symbolic link is recorded by its target, not followed. A test project at the root is not
/// hashed: it would cover the production code too.
/// </summary>
internal static class TestFileHashes
{
    private const string ReceivedMarker = ".received.";
    private const string SourceExtension = ".cs";
    private const string RootProject = ".";
    private const string UnreadableHash = "unreadable";

    private static readonly HashSet<string> BuildDirectories = new(["bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OutputDirectories = new(["TestResults"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EditorExtensions = new([".user", ".swp", ".swo", ".tmp"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> EditorFiles = new(["Thumbs.db", "desktop.ini"], StringComparer.OrdinalIgnoreCase);

    /// <param name="testProjects">Test project directories relative to <paramref name="root"/>, '/'-separated ("." for the root).</param>
    /// <returns>Each file's hash by its path relative to the root, '/'-separated.</returns>
    public static IReadOnlyDictionary<string, string> Compute(string root, IReadOnlyList<string> testProjects)
    {
        SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string directory in testProjects
            .Where(project => project is not (RootProject or ""))
            .Select(project => Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists))
        {
            foreach (string file in Files(directory, insideOutput: false))
            {
                hashes[Relative(root, file)] = Hash(file);
            }
        }

        return hashes;
    }

    /// <summary>The hash of one file relative to the root; null when it does not exist.</summary>
    public static string? Of(string root, string file)
    {
        string path = Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Hash(path) : null;
    }

    public static TestFileChanges Compare(IReadOnlyDictionary<string, string> recorded, IReadOnlyDictionary<string, string> current) => new(
        [.. recorded.Where(entry => current.TryGetValue(entry.Key, out string? hash) && hash != entry.Value).Select(entry => entry.Key).Order(StringComparer.Ordinal)],
        [.. current.Keys.Where(file => !recorded.ContainsKey(file)).Order(StringComparer.Ordinal)],
        [.. recorded.Keys.Where(file => !current.ContainsKey(file)).Order(StringComparer.Ordinal)]);

    /// <summary>
    /// Why the open cycle's test files are not the ones its red accepted, with what to do; null when they are, or when the cycle was
    /// recorded without hashes.
    /// </summary>
    public static string? Refusal(string root, RedGreenRecord record)
    {
        if (record.OpenCycle is not RedGreenCycle open || open.TestFiles.Count == 0)
        {
            return null;
        }

        TestFileChanges changes = Compare(open.TestFiles, Compute(root, record.TestProjects));
        return changes.Any
            ? $"{changes.Describe()} changed after red cycle {open.Number} was accepted, without a recorded amend. A locked test file changes only with a "
                + $"person's amend: ask them to run /lockflight:amend <Class.Method> in the session, or '{EngineCommand.Linked} redgreen amend <Class.Method> --reason \"<why>\"' "
                + $"in a terminal, and then run '{EngineCommand.Linked} redgreen red' to re-check the test; or restore the file as it was at red"
                + (open.Snapshot is null ? string.Empty : $" (the red-time snapshot {RedGreenSession.Short(open.Snapshot)} holds it)")
            : null;
    }

    private static IEnumerable<string> Files(string directory, bool insideOutput)
    {
        foreach (string file in Directory.EnumerateFiles(directory).Where(file => Compared(file, insideOutput)))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(child);
            if (BuildDirectories.Contains(name) || name.StartsWith('.'))
            {
                continue;
            }

            if (new DirectoryInfo(child).LinkTarget is not null)
            {
                yield return child;
                continue;
            }

            foreach (string file in Files(child, insideOutput || OutputDirectories.Contains(name)))
            {
                yield return file;
            }
        }
    }

    private static bool Compared(string file, bool insideOutput)
    {
        string name = Path.GetFileName(file);
        bool source = Path.GetExtension(name).Equals(SourceExtension, StringComparison.OrdinalIgnoreCase);
        return !name.StartsWith('.')
            && !EditorFiles.Contains(name)
            && !EditorExtensions.Contains(Path.GetExtension(name))
            && (source || (!insideOutput && !name.Contains(ReceivedMarker, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>A file's content hash; a symbolic link's is that of its target path, and a file that cannot be read gets a fixed marker.</summary>
    private static string Hash(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is string target)
            {
                return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"link:{target}")));
            }

            return Convert.ToHexStringLower(SHA256.HashData(WithoutCarriageReturns(File.ReadAllBytes(path))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UnreadableHash;
        }
    }

    /// <summary>
    /// A text file with its CRLF line endings read as LF, so a checkout that converts line endings (<c>core.autocrlf</c> on Windows) is
    /// not a change; a file with a NUL byte is binary and kept as it is.
    /// </summary>
    private static byte[] WithoutCarriageReturns(byte[] content)
    {
        if (Array.IndexOf(content, (byte)0) >= 0)
        {
            return content;
        }

        List<byte> normalized = new(content.Length);
        for (int index = 0; index < content.Length; index++)
        {
            if (content[index] != '\r' || index + 1 >= content.Length || content[index + 1] != '\n')
            {
                normalized.Add(content[index]);
            }
        }

        return [.. normalized];
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
