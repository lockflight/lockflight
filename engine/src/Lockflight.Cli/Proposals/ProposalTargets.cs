using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// Checks a request against the project before any patch is made: the file must lie inside the project (not in <c>.git</c>, not a
/// directory), the new content must come from outside the protected paths (a protected file cannot be smuggled in as the source),
/// and both sides must be text that differs.
/// </summary>
internal static class ProposalTargets
{
    /// <summary>How far into a file git looks for a NUL byte to call it binary.</summary>
    private const int BinaryProbeLength = 8000;

    private const string GitDirectory = ".git";

    /// <param name="root">The project root, canonical (<see cref="PathRules.Canonicalize"/>).</param>
    public static ProposalTargetResolution Resolve(ProposalRequest request, string root, IReadOnlyList<string> protectedGlobs, string workingDirectory)
    {
        string fullPath = PathRules.Canonicalize(request.Path, workingDirectory);
        string? relative = PathRules.ToRelative(root, fullPath);
        if (relative is null or { Length: 0 })
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' is outside the project ({root}); propose only files of this project");
        }

        if (relative.Split('/').Contains(GitDirectory, StringComparer.OrdinalIgnoreCase))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' is inside {GitDirectory}, which is git's own data, not a project file");
        }

        if (Directory.Exists(fullPath))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' is a directory; propose each file in it");
        }

        bool exists = File.Exists(fullPath);
        if (request.ContentPath is null && !exists)
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' does not exist, so there is nothing to delete");
        }

        byte[]? oldContent = exists ? File.ReadAllBytes(fullPath) : null;
        byte[]? newContent = null;
        if (request.ContentPath is string contentPath)
        {
            if (ContentRefusal(contentPath, root, protectedGlobs, workingDirectory) is string refusal)
            {
                return ProposalTargetResolution.Refused(refusal);
            }

            newContent = File.ReadAllBytes(PathRules.Canonicalize(contentPath, workingDirectory));
        }

        if (IsBinary(oldContent) || IsBinary(newContent))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' is binary (it contains NUL bytes); a text patch cannot carry it, so ask the person to replace the file");
        }

        if (oldContent is not null && newContent is not null && oldContent.AsSpan().SequenceEqual(newContent))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' already has this content; there is nothing to propose");
        }

        UnixFileMode? mode = exists && !OperatingSystem.IsWindows() ? File.GetUnixFileMode(fullPath) : null;
        string? rule = protectedGlobs.FirstOrDefault(glob => PathRules.Matches(glob, relative));
        return ProposalTargetResolution.Resolved(new ProposalTarget(relative, oldContent, newContent, mode, rule, HighRiskFiles.Reason(relative)));
    }

    private static string? ContentRefusal(string contentPath, string root, IReadOnlyList<string> protectedGlobs, string workingDirectory)
    {
        string fullPath = PathRules.Canonicalize(contentPath, workingDirectory);
        if (PathRules.ToRelative(root, fullPath) is { Length: > 0 } relative
            && protectedGlobs.FirstOrDefault(glob => PathRules.Matches(glob, relative)) is string rule)
        {
            return $"the new content '{contentPath}' is itself protected by rule '{rule}'; write it outside the protected paths, for example in your scratch directory";
        }

        return File.Exists(fullPath) ? null : $"the new content '{contentPath}' does not exist; write the complete new file there first";
    }

    private static bool IsBinary(byte[]? content) => content is not null && content.AsSpan(0, Math.Min(content.Length, BinaryProbeLength)).Contains((byte)0);
}
