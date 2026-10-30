using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// Reads the routing policy from the base branch with git (<c>&lt;base&gt;:lockflight.yaml</c>), never from the working tree, so
/// neither the checkout nor the pull request's own manifest can change it. Its blob id is recorded with the policy, so a class is
/// reproducible for that policy.
/// </summary>
internal static class RoutingPolicyReader
{
    public static async Task<RoutingPolicyReading> ReadAsync(string projectRoot, string baseRef, CancellationToken cancellationToken)
    {
        if (baseRef.Trim().Length == 0 || RoutingGit.IsZeroId(baseRef.Trim()) || await Git.CommitOfAsync(projectRoot, baseRef, cancellationToken) is not string commit)
        {
            return new RoutingPolicyReading(RoutingPolicyReadStatus.NoBase, null, $"the base '{baseRef}' names no commit in this clone");
        }

        if (await RoutingGit.PrefixAsync(projectRoot, cancellationToken) is not string prefix
            || await RoutingGit.BlobsAsync(projectRoot, commit, [prefix + Manifest.FileName], cancellationToken) is not IReadOnlyDictionary<string, byte[]> blobs)
        {
            return new RoutingPolicyReading(RoutingPolicyReadStatus.GitFailed, null, $"git could not read {Manifest.FileName} at {baseRef}");
        }

        if (!blobs.TryGetValue(prefix + Manifest.FileName, out byte[]? content))
        {
            return new RoutingPolicyReading(
                RoutingPolicyReadStatus.NoRoutingSection,
                RoutingPolicy.From(null, new PolicySource(baseRef, commit, ManifestBlob: null, RoutingPresetNames.ToName(RoutingPreset.Default))),
                $"no {Manifest.FileName} at {baseRef}: the default routing preset applies");
        }

        string text = Encoding.UTF8.GetString(content).TrimStart('﻿');
        ManifestLoadResult parsed = new ManifestParser(projectRoot, $"{Manifest.FileName} at {baseRef}").Parse(text);
        if (parsed.Manifest is not Manifest manifest)
        {
            return new RoutingPolicyReading(RoutingPolicyReadStatus.InvalidPolicy, null, $"the base branch's {Manifest.FileName} is invalid: {parsed.Error?.Describe()}");
        }

        string? blob = await RoutingGit.BlobIdAsync(projectRoot, commit, prefix + Manifest.FileName, cancellationToken);
        PolicySource source = new(baseRef, commit, blob, RoutingPresetNames.ToName(manifest.Routing.Preset));
        RoutingPolicy policy = RoutingPolicy.From(manifest, source);
        return manifest.Routing.FromManifest
            ? new RoutingPolicyReading(RoutingPolicyReadStatus.Read, policy, string.Empty)
            : new RoutingPolicyReading(RoutingPolicyReadStatus.NoRoutingSection, policy, $"{Manifest.FileName} at {baseRef} has no routing section: the default preset applies");
    }
}
