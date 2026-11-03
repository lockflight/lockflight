using System.Text.RegularExpressions;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Checks a requirement marked <c>[outside .NET: …]</c> against the files it names: each must lie inside the project, exist and name
/// the requirement ID as a whole word (plain text: a Vitest or Jest test names it in a title or a comment). Lockflight runs none of
/// those tests; the check only shows that each pointer leads to a file that claims the requirement, which a reviewer then reads.
/// A path under <c>specs/</c> never counts (the spec names its own requirements), and neither does a directory or a symbolic link.
/// </summary>
internal static class OutsideVerification
{
    private const string SpecsDirectory = "specs/";

    /// <summary>
    /// A path relative to the project root that does not leave it: not rooted, no drive letter, and no segment of dots and spaces only
    /// (<c>..</c>, <c>.</c>, or <c>.. </c>, which Windows reads as the parent).
    /// </summary>
    public static bool IsInsideProject(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Length > 0
            && !normalized.StartsWith('/')
            && !Path.IsPathRooted(path)
            && !(normalized.Length >= 2 && normalized[1] == ':')
            && !normalized.Split('/').Any(segment => segment.Length > 0 && segment.Trim(' ', '.').Length == 0);
    }

    /// <param name="content">The file's text; null when it does not exist.</param>
    public static OutsideFile Judge(string requirementId, string path, string? content) =>
        !IsInsideProject(path) ? new OutsideFile(path, OutsideFileState.OutsideProject)
        : IsInSpecs(path) ? new OutsideFile(path, OutsideFileState.InSpecs)
        : content is null ? new OutsideFile(path, OutsideFileState.Missing)
        : Regex.IsMatch(content, $@"(?<![\w-]){Regex.Escape(requirementId)}(?![\w-])", RegexOptions.CultureInvariant)
            ? new OutsideFile(path, OutsideFileState.Names)
            : new OutsideFile(path, OutsideFileState.DoesNotName);

    /// <summary>Whether a path (relative, <c>/</c>- or <c>\</c>-separated) lies under the project's <c>specs/</c> directory.</summary>
    public static bool IsInSpecs(string path) => path.Replace('\\', '/').StartsWith(SpecsDirectory, StringComparison.OrdinalIgnoreCase);

    /// <summary>The requirement's files as they are in the working tree under <paramref name="root"/>.</summary>
    public static IReadOnlyList<OutsideFile> ReadLocal(Requirement requirement, string root) =>
        [.. requirement.VerifiedOutside.Select(path => Judge(requirement.Id, path, IsInsideProject(path) && !IsInSpecs(path) ? ReadOrNull(root, path) : null))];

    /// <summary>The file's text; null when it is missing, a directory or a symbolic link (which could lead out of the project).</summary>
    private static string? ReadOrNull(string root, string path)
    {
        try
        {
            FileInfo file = new(Path.Combine(root, path.Replace('\\', '/')));
            return file.Exists && file.LinkTarget is null ? File.ReadAllText(file.FullName) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
