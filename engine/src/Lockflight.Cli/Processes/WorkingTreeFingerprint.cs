using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Processes;

/// <summary>
/// One hash for the state of the files under a directory: the commit HEAD points at, plus the path and bytes of every file that
/// differs from it (changed, staged, deleted, or untracked and not ignored). Two equal fingerprints mean nothing a build or a test
/// run reads from the repository changed in between. The engine's own directories (<c>.lockflight/</c>, <c>.claude/</c>) are left
/// out: it writes there itself.
/// </summary>
internal static class WorkingTreeFingerprint
{
    private const string Executable = "git";
    private const string DeletedMark = "deleted";

    private static readonly string[] Excluded = [".lockflight/", ".claude/"];

    /// <summary>The fingerprint of <paramref name="directory"/>; null when git cannot tell (not a repository).</summary>
    public static async Task<string?> OfAsync(string directory, CancellationToken cancellationToken)
    {
        if (await Git.UncommittedBaseAsync(directory, cancellationToken) is not string baseTree)
        {
            return null;
        }

        string head = await Git.HeadAsync(directory, cancellationToken) ?? baseTree;
        ProcessResult changed = await ProcessRunner.RunAsync(
            Executable, ["-c", "core.quotePath=false", "diff", baseTree, "--name-only", "--no-renames", "--relative", "-z", "--"], directory, cancellationToken);
        ProcessResult untracked = await ProcessRunner.RunAsync(
            Executable, ["-c", "core.quotePath=false", "ls-files", "--others", "--exclude-standard", "-z", "--"], directory, cancellationToken);
        if (changed.ExitCode != 0 || untracked.ExitCode != 0)
        {
            return null;
        }

        string[] paths = [.. (changed.Output + "\0" + untracked.Output).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !Excluded.Any(excluded => path.StartsWith(excluded, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{head}\n"));
        foreach (string path in paths)
        {
            string file = Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
            string content = File.Exists(file) ? Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, cancellationToken))) : DeletedMark;
            hash.AppendData(Encoding.UTF8.GetBytes($"{path}\0{content}\n"));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
