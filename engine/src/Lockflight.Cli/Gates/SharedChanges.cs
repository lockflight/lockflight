using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Changes in the project's own working tree outside every unit that can change how units build or test: C# sources, project and
/// MSBuild files, solutions, SDK and package configuration (a shared <c>libs/</c> folder a unit references, a root
/// <c>Directory.Build.props</c>). Such a change makes every .NET unit "changed" for the Stop gate and is part of each unit's cache
/// key, so a unit's cached PASS never outlives an edit to code it depends on.
/// </summary>
internal static class SharedChanges
{
    private static readonly string[] BuildInputs = ["*.cs", "*.csproj", "*.fsproj", "*.props", "*.targets", "*.sln", "*.slnx", "*.rsp", "global.json", "nuget.config", "NuGet.config", "*.editorconfig", "*.globalconfig"];

    /// <summary>A fingerprint of the shared build inputs' uncommitted state; null when there are no such changes.</summary>
    public static async Task<string?> FingerprintAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        string[] pathspecs = [.. BuildInputs.Select(pattern => $":(glob)**/{pattern}"), .. manifest.Units.Select(unit => $":(exclude){unit.Path}")];
        ProcessResult diff = await ProcessRunner.RunAsync("git", ["diff", "HEAD", "--binary", "--no-ext-diff", "--no-color", "--", .. pathspecs], manifest.Root, cancellationToken);
        IReadOnlyList<string>? untracked = await Git.UntrackedFilesAsync(manifest.Root, pathspecs, Timeout.InfiniteTimeSpan, cancellationToken);
        if (diff.ExitCode != 0 || untracked is null)
        {
            // git failed: treat it as a shared change, so units are judged and nothing is served from the cache.
            return Guid.NewGuid().ToString();
        }

        if (diff.Output.Length == 0 && untracked.Count == 0)
        {
            return null;
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(diff.Output));
        foreach (string file in untracked.Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file));
            string path = Path.Combine(manifest.Root, file);
            if (File.Exists(path))
            {
                hash.AppendData(await File.ReadAllBytesAsync(path, cancellationToken));
            }
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
