using System.Security.Cryptography;

namespace Lockflight.Cli.Review;

/// <summary>
/// Which changed files the reviewer is given as a listing (path, size, content hash) instead of their diff. The choice is by path
/// only, from a fixed list: the package lock files (<c>packages.lock.json</c>, <c>package-lock.json</c>, <c>pnpm-lock.yaml</c>,
/// <c>yarn.lock</c>), an EF Core model snapshot (<c>Migrations/*ModelSnapshot.cs</c>), and a migration's designer file
/// (<c>Migrations/&lt;name&gt;.Designer.cs</c>) when its migration <c>&lt;name&gt;.cs</c> is in the diff or the tree (the
/// migration itself stays in the diff). A snapshot or designer file whose content does not look like EF Core's generated code
/// (<see cref="EfGeneratedCode"/>) is shown in full. A file's own marks (an <c>// &lt;auto-generated /&gt;</c> header alone, or
/// <c>linguist-generated</c> in <c>.gitattributes</c>) are never trusted: the change under review could add them. A file that is not
/// in the working tree (deleted), is a symbolic link, or whose path git quoted, is shown in full. Names are compared ignoring case,
/// as the guard's globs are.
/// </summary>
internal static class GeneratedFiles
{
    public const string Status = "listed, not reviewed";

    private const string MigrationsDirectory = "Migrations";
    private const string DesignerSuffix = ".Designer.cs";
    private const string SnapshotSuffix = "ModelSnapshot.cs";

    private static readonly string[] LockFiles = ["packages.lock.json", "package-lock.json", "pnpm-lock.yaml", "yarn.lock"];

    /// <summary>The listing of a changed file the reviewer is not shown; null when its diff is shown.</summary>
    public static GeneratedFile? Find(string path, IReadOnlySet<string> changedPaths, string root)
    {
        if (path.Contains('\\', StringComparison.Ordinal) || !IsGeneratedPath(path, changedPaths, root))
        {
            return null;
        }

        FileInfo file = new(Path.Combine(root, path));
        if (!file.Exists || file.LinkTarget is not null)
        {
            return null;
        }

        byte[] content = File.ReadAllBytes(file.FullName);
        if (!IsLockFile(path) && !LooksGenerated(path, File.ReadAllText(file.FullName)))
        {
            return null;
        }

        return new GeneratedFile(path, content.LongLength, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    /// <summary>
    /// Whether a file of one revision is on the fixed generated list, from its path and text alone: a lock file, or a snapshot or
    /// designer file whose text has EF Core's shape (a designer only when <paramref name="exists"/> says its migration is there).
    /// Routing reads both sides of a change this way, without the working tree.
    /// </summary>
    public static bool IsGenerated(string path, string? content, Func<string, bool> exists) =>
        IsLockFile(path) || (content is not null && IsGeneratedPath(path, exists) && LooksGenerated(path, content));

    /// <summary>The note after the shown diff that lists the generated files, or nothing when there are none.</summary>
    public static string Note(IReadOnlyList<GeneratedFile> files) =>
        files.Count == 0
            ? string.Empty
            : $"\n[{files.Count} generated {(files.Count == 1 ? "file" : "files")} {Status}: the diff above leaves them out by path (package lock files, "
                + "EF Core model snapshots and migration designer files); read one when the change depends on it:\n"
                + string.Concat(files.Select(file => $"- {file.Path} ({file.Bytes} bytes, sha256 {file.Sha256})\n"))
                + "]";

    private static bool IsGeneratedPath(string path, IReadOnlySet<string> changedPaths, string root) =>
        IsGeneratedPath(path, migration => changedPaths.Contains(migration) || File.Exists(Path.Combine(root, migration)));

    private static bool IsGeneratedPath(string path, Func<string, bool> exists)
    {
        if (IsLockFile(path))
        {
            return true;
        }

        if (!InMigrations(path))
        {
            return false;
        }

        if (path.EndsWith(SnapshotSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!path.EndsWith(DesignerSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return exists(path[..^DesignerSuffix.Length] + ".cs");
    }

    private static bool LooksGenerated(string path, string content) =>
        path.EndsWith(SnapshotSuffix, StringComparison.OrdinalIgnoreCase) ? EfGeneratedCode.IsModelSnapshot(content) : EfGeneratedCode.IsMigrationDesigner(content);

    private static bool IsLockFile(string path) => LockFiles.Contains(FileName(path), StringComparer.OrdinalIgnoreCase);

    private static bool InMigrations(string path)
    {
        int slash = path.LastIndexOf('/');
        string directory = slash < 0 ? string.Empty : path[..slash];
        return directory.Equals(MigrationsDirectory, StringComparison.OrdinalIgnoreCase)
            || directory.EndsWith("/" + MigrationsDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];
}
