using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Reads the policy from a commit with <c>git show</c>, so neither the checkout nor the pull request's own <c>lockflight.yaml</c>
/// can change what the judge enforces.
/// </summary>
internal static class BasePolicyReader
{
    public static async Task<BasePolicyReading> ReadAsync(string root, string commit, CancellationToken cancellationToken)
    {
        string[] present = [.. await PresentAsync(root, commit, cancellationToken)];
        if (await JudgeGit.ShowFileAsync(root, commit, Manifest.FileName, cancellationToken) is not string text)
        {
            return new BasePolicyReading(new BasePolicy(commit, Manifest: null, ManifestSha256: null, ManifestBlob: null, present), Error: null);
        }

        string label = $"{Manifest.FileName} at {CommitIds.Short(commit)}";
        ManifestLoadResult parsed = new ManifestParser(root, label).Parse(text);
        if (parsed.Manifest is not Manifest manifest)
        {
            return new BasePolicyReading(null, $"the target branch's {Manifest.FileName} is invalid: {parsed.Error?.Describe()}");
        }

        string sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        string? blob = await JudgeGit.BlobOfAsync(root, commit, Manifest.FileName, cancellationToken);
        return new BasePolicyReading(new BasePolicy(commit, manifest, sha256, blob, present), Error: null);
    }

    private static async Task<IEnumerable<string>> PresentAsync(string root, string commit, CancellationToken cancellationToken)
    {
        List<string> present = [];
        foreach (string file in CiShellScripts.PolicyFiles.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (await JudgeGit.BlobOfAsync(root, commit, file, cancellationToken) is not null)
            {
                present.Add(file);
            }
        }

        return present;
    }
}
