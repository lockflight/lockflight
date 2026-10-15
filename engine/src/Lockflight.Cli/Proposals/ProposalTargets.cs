using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Proposals;

/// <summary>
/// Checks a request against the project before any patch is made: the file must lie inside the project (not in <c>.git</c>, not a
/// directory) after symbolic links are resolved, its name must be printable as it is, the new content must come from outside the
/// protected paths (a protected file cannot be smuggled in as the source), and both sides must be text of a bounded size that
/// differs. Every check and every read uses the resolved path, so the file the patch names is the file that was read.
/// </summary>
internal static class ProposalTargets
{
    /// <summary>The largest file a proposal reads, on either side: a text patch is for a person to read, and a device such as /dev/zero never ends.</summary>
    public const int MaxContentBytes = 8 * 1024 * 1024;

    /// <summary>How far into a file git looks for a NUL byte to call it binary.</summary>
    private const int BinaryProbeLength = 8000;

    private const int ReadBufferBytes = 81920;

    private const string GitDirectory = ".git";

    /// <param name="root">The project root, canonical (<see cref="PathRules.Canonicalize"/>).</param>
    public static ProposalTargetResolution Resolve(ProposalRequest request, string root, IReadOnlyList<string> protectedGlobs, string workingDirectory)
    {
        if (!TerminalText.IsPlainName(request.Path))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' has a control character in its name, which cannot be shown to the person as it is; rename the file first");
        }

        string fullPath = PathRules.Canonicalize(request.Path, workingDirectory);
        string? resolved = PathRules.ToRelative(root, fullPath);
        if (resolved is null or { Length: 0 })
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' is outside the project ({root}); propose only files of this project");
        }

        string relative = OnDiskNames.Spell(root, resolved);
        if (!TerminalText.IsPlainName(relative))
        {
            return ProposalTargetResolution.Refused($"'{request.Path}' leads to a file with a control character in its name, which cannot be shown to the person as it is; rename the file first");
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

        byte[]? oldContent = exists ? ReadBounded(fullPath) : null;
        if (exists && oldContent is null)
        {
            return ProposalTargetResolution.Refused(TooLarge(request.Path));
        }

        byte[]? newContent = null;
        if (request.ContentPath is string contentPath)
        {
            string contentFullPath = PathRules.Canonicalize(contentPath, workingDirectory);
            if (ContentRefusal(contentPath, contentFullPath, root, protectedGlobs) is string refusal)
            {
                return ProposalTargetResolution.Refused(refusal);
            }

            newContent = ReadBounded(contentFullPath);
            if (newContent is null)
            {
                return ProposalTargetResolution.Refused(TooLarge(contentPath));
            }
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

    private static string? ContentRefusal(string contentPath, string fullPath, string root, IReadOnlyList<string> protectedGlobs)
    {
        if (PathRules.ToRelative(root, fullPath) is { Length: > 0 } relative
            && protectedGlobs.FirstOrDefault(glob => PathRules.Matches(glob, OnDiskNames.Spell(root, relative))) is string rule)
        {
            return $"the new content '{contentPath}' is itself protected by rule '{rule}'; write it outside the protected paths, for example in your scratch directory";
        }

        return File.Exists(fullPath) ? null : $"the new content '{contentPath}' does not exist; write the complete new file there first";
    }

    /// <summary>The file's bytes; null when it holds more than <see cref="MaxContentBytes"/>.</summary>
    private static byte[]? ReadBounded(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using MemoryStream content = new();
        byte[] buffer = new byte[ReadBufferBytes];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            if (content.Length + read > MaxContentBytes)
            {
                return null;
            }

            content.Write(buffer, 0, read);
        }

        return content.ToArray();
    }

    private static string TooLarge(string path) =>
        $"'{path}' is larger than {MaxContentBytes / (1024 * 1024)} MB (or never ends, like a device); a proposal is a text patch for a person to read, so ask the person to replace the file";

    private static bool IsBinary(byte[]? content) => content is not null && content.AsSpan(0, Math.Min(content.Length, BinaryProbeLength)).Contains((byte)0);
}
