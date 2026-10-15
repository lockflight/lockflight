using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates;

/// <summary>
/// The working-tree state of one unit of a workspace: whether it has uncommitted or new files, and a fingerprint of that
/// state plus everything else its gate run depends on — the Stop gate's per-unit cache key. A unit that is its own repository is
/// asked of its own git; a monorepo unit is the shell's git limited to its directory.
/// </summary>
internal sealed record UnitState(bool Dirty, string? Fingerprint)
{
    public static async Task<UnitState> ReadAsync(Manifest manifest, Unit unit, string engineAndSettings, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(manifest.Root, unit.Path.Replace('/', Path.DirectorySeparatorChar));
        bool ownRepository = Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git"));
        string gitRoot = ownRepository ? directory : manifest.Root;
        string[] pathspec = ownRepository ? [] : [unit.Path];
        if (!Directory.Exists(directory) || await Git.HeadAsync(gitRoot, cancellationToken) is not string head)
        {
            // Not cloned, or not a repository with a commit: nothing to judge and nothing to cache.
            return new UnitState(Dirty: false, Fingerprint: null);
        }

        ProcessResult diff = await ProcessRunner.RunAsync("git", ["diff", "HEAD", "--binary", "--no-ext-diff", "--no-color", "--", .. pathspec], gitRoot, cancellationToken);
        IReadOnlyList<string>? untracked = await Git.UntrackedFilesAsync(gitRoot, pathspec, Timeout.InfiniteTimeSpan, cancellationToken);
        if (diff.ExitCode != 0 || untracked is null)
        {
            // git failed: judge the unit (dirty) and never serve a cached PASS for it.
            return new UnitState(Dirty: true, Fingerprint: null);
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, engineAndSettings);
        Append(hash, unit.ToString());
        Append(hash, head);
        Append(hash, diff.Output);
        foreach (string file in untracked.Order(StringComparer.Ordinal))
        {
            Append(hash, file);
            string path = Path.Combine(gitRoot, file);
            if (File.Exists(path))
            {
                hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
            }
        }

        return new UnitState(Dirty: diff.Output.Length > 0 || untracked.Count > 0, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }
}
